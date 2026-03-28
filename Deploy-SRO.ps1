#Requires -Version 5.0
<#
.SYNOPSIS
    Deploy automatico de ServicioReportesOracle al servidor del cliente.
    Copia el core + UI, detiene/inicia el servicio Windows, no toca configs.

.USAGE
    .\Deploy-SRO.ps1 -Servidor 192.168.1.17 -Usuario "CUPDOMI\soportebit"
    .\Deploy-SRO.ps1 -Servidor 192.168.1.17 -Usuario "CUPDOMI\soportebit" -DryRun

.NOTAS
    - Requiere VPN conectada al cliente
    - Si da error de TrustedHosts, ejecutar una vez como admin en tu PC:
        Set-Item WSMan:\localhost\Client\TrustedHosts -Value "192.168.1.17" -Force
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Servidor,

    [string]$Usuario = "Administrador",

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# ============================================================
# CONFIGURACION
# ============================================================

$repoRaiz        = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseCore     = "$repoRaiz\ServicioReportesOracle\bin\Release"
$releaseUI       = "$repoRaiz\ServicioReportesOracle.UI\bin\Release"
$rutaServidor    = "C:\ServicioReportesOracle"
$nombreServicio  = "ServicioReportesOracle"  # nombre interno (sc create)
$msbuild         = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"

$archivosProtegidos = @(
    "config.json",
    "Config.json",
    "consultas.json",
    "Consultas.json",
    "consultas_soap.json",
    "filters.json",
    "ids_history.json",
    "status.json",
    "ws_estado.json",
    "comparaciones_pendientes.json",
    "alertas_oracle_enviadas.json",
    "ui_settings.json",
    "install.bat",
    "uninstall.bat",
    "testsoap.txt"
)

# ============================================================

function Write-Header($t) {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  $t" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}
function Write-OK($t)    { Write-Host "  [OK]  $t" -ForegroundColor Green }
function Write-WARN($t)  { Write-Host "  [!!]  $t" -ForegroundColor Yellow }
function Write-ERR($t)   { Write-Host "  [XX]  $t" -ForegroundColor Red }
function Write-INFO($t)  { Write-Host "  [--]  $t" -ForegroundColor Gray }
function Write-STEP($n,$t) { Write-Host "`n  [$n] $t" -ForegroundColor White }

$tiempoInicio = Get-Date

Write-Header "DEPLOY ServicioReportesOracle -- $(Get-Date -Format 'dd/MM/yyyy HH:mm')"
if ($DryRun) { Write-WARN "MODO DRY RUN -- no se aplicara ningun cambio" }

# ============================================================
# PASO 0: Compilar Release
# ============================================================
Write-STEP "0" "Compilando Release con MSBuild..."

if (-not (Test-Path $msbuild)) {
    Write-ERR "MSBuild no encontrado en: $msbuild"
    Write-INFO "  Ajusta la variable $msbuild en el script."
    exit 1
}

$sln = "$repoRaiz\ServicioReportesOracle.sln"
if (-not (Test-Path $sln)) {
    Write-ERR "Solucion no encontrada: $sln"
    exit 1
}

if ($DryRun) {
    Write-WARN "  [DRY RUN] Se compilaria: $sln"
} else {
    Write-INFO "  Ejecutando rebuild Release..."
    $buildOutput = & $msbuild $sln -p:Configuration=Release -t:Rebuild -m -nologo -verbosity:minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-ERR "Build FALLO -- deploy cancelado"
        $buildOutput | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        exit 1
    }
    Write-OK "Build exitoso"
}

# Verificar que los exes existen post-build
$errores = 0
foreach ($par in @(
    @{Ruta=$releaseCore; Exe="ServicioReportesOracle.exe";    Nombre="Core"},
    @{Ruta=$releaseUI;   Exe="ServicioReportesOracle.UI.exe"; Nombre="UI"}
)) {
    $exePath = Join-Path $par.Ruta $par.Exe
    if (Test-Path $exePath) {
        $ver = (Get-Item $exePath).VersionInfo.FileVersion
        Write-OK "$($par.Nombre): $($par.Exe)$(if($ver){" (v$ver)"})"
    } else {
        Write-ERR "$($par.Nombre) no encontrado post-build: $exePath"
        $errores++
    }
}
if ($errores -gt 0) { exit 1 }

# ============================================================
# PASO 1: Credenciales + conexion SMB
# ============================================================
Write-STEP "1" "Conectando a $Servidor..."

# Construir nombre de usuario correcto
if ($Usuario -match '\\' -or $Usuario -match '@') {
    $usuarioCredencial = $Usuario
} else {
    $usuarioCredencial = "$Servidor\$Usuario"
}

Write-Host "  Usuario: $usuarioCredencial" -ForegroundColor Gray
$passwordSegura = Read-Host "  Contrasena" -AsSecureString
$cred = New-Object System.Management.Automation.PSCredential(
    [string]$usuarioCredencial,
    $passwordSegura
)

# Conectar SMB
try {
    if (Get-PSDrive -Name "SRODeploy" -ErrorAction SilentlyContinue) {
        Remove-PSDrive -Name "SRODeploy" -Force
    }
    New-PSDrive -Name "SRODeploy" -PSProvider FileSystem `
                -Root "\\$Servidor\C$" -Credential $cred | Out-Null
    Write-OK "SMB conectado a \\$Servidor\C$"
} catch {
    Write-ERR "SMB fallo: $($_.Exception.Message)"
    exit 1
}

$rutaDestinoSMB = "SRODeploy:" + $rutaServidor.Substring(2)

# Verificar/crear carpeta destino
if (-not (Test-Path $rutaDestinoSMB)) {
    Write-WARN "La carpeta $rutaServidor no existe en el servidor."
    if (-not $DryRun) {
        New-Item -ItemType Directory -Path $rutaDestinoSMB -Force | Out-Null
        Write-OK "Carpeta creada"
    }
}

# Version actual en servidor
$exeRemoto = "$rutaDestinoSMB\ServicioReportesOracle.exe"
if (Test-Path $exeRemoto) {
    $versionAntes = (Get-Item $exeRemoto).VersionInfo.FileVersion
} else {
    $versionAntes = "no instalado"
}

# ============================================================
# PASO 2: Detener el servicio
# ============================================================
Write-STEP "2" "Deteniendo servicio '$nombreServicio'..."

$nombreServicioInterno = ""

if ($DryRun) {
    Write-WARN "  [DRY RUN] Se detendria '$nombreServicio'"
} else {
    try {
        $resultado = Invoke-Command -ComputerName $Servidor -Credential $cred -ScriptBlock {
            param($svc)
            $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
            if (-not $s) { return @{ Estado = "NotFound"; Nombre = "" } }
            $estadoPrevio = $s.Status.ToString()
            if ($s.Status -eq "Running") {
                Stop-Service -Name $s.Name -Force
                $s.WaitForStatus("Stopped", (New-TimeSpan -Seconds 15))
            }
            return @{ Estado = $estadoPrevio; Nombre = $s.Name }
        } -ArgumentList $nombreServicio

        if ($resultado.Estado -eq "NotFound") {
            Write-WARN "Servicio '$nombreServicio' no encontrado (primera instalacion?)"
        } else {
            $nombreServicioInterno = $resultado.Nombre
            Write-OK "Servicio detenido (estaba: $($resultado.Estado))"
        }
    } catch {
        Write-ERR "No se pudo detener el servicio: $($_.Exception.Message)"
        Write-INFO "  Si es error de TrustedHosts, ejecuta en tu PC como admin:"
        Write-INFO "  Set-Item WSMan:\localhost\Client\TrustedHosts -Value '$Servidor' -Force"
        Remove-PSDrive -Name "SRODeploy" -Force -ErrorAction SilentlyContinue
        exit 1
    }
}

# ============================================================
# PASO 3: Copiar Core
# ============================================================
Write-STEP "3" "Empaquetando Release en ZIP..."

$zipTemp = "$env:TEMP\SRODeploy_$(Get-Date -Format 'yyyyMMdd_HHmmss').zip"

if ($DryRun) {
    $totalArchivos = (
        (Get-ChildItem $releaseCore -File | Where-Object { $_.Name -notin $archivosProtegidos }).Count +
        (Get-ChildItem $releaseUI   -File | Where-Object { $_.Name -notin $archivosProtegidos }).Count
    )
    Write-WARN "  [DRY RUN] Se empaquetarian ~$totalArchivos archivos en un ZIP y se copiaria al servidor"
    $copiadosCore = $totalArchivos
    $copiadosUI   = 0
} else {
    # Crear ZIP con archivos de Core y UI (excluyendo configs del cliente)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipStream = [System.IO.Compression.ZipFile]::Open($zipTemp, "Create")

    $copiadosCore = 0
    Get-ChildItem $releaseCore -File | Where-Object { $_.Name -notin $archivosProtegidos } | ForEach-Object {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zipStream, $_.FullName, $_.Name, "Optimal") | Out-Null
        $copiadosCore++
    }

    $copiadosUI = 0
    Get-ChildItem $releaseUI -File | Where-Object { $_.Name -notin $archivosProtegidos } | ForEach-Object {
        # Solo agregar si no esta ya en el ZIP (UI comparte DLLs con Core)
        $entryExiste = $zipStream.Entries | Where-Object { $_.Name -eq $_.Name }
        if (-not ($zipStream.Entries | Where-Object { $_.FullName -eq $_.Name })) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zipStream, $_.FullName, $_.Name, "Optimal") | Out-Null
        }
        $copiadosUI++
    }

    $zipStream.Dispose()
    $zipSize = [math]::Round((Get-Item $zipTemp).Length / 1MB, 1)
    Write-OK "ZIP creado: $zipSize MB ($copiadosCore core + $copiadosUI UI archivos)"

    # ============================================================
    # PASO 4: Copiar ZIP al servidor y descomprimir
    # ============================================================
    Write-STEP "4" "Copiando ZIP al servidor (1 transferencia)..."

    # Ruta UNC directa — mas confiable que el drive mapeado para un solo archivo
    $zipRemotoUNC  = "\\$Servidor\C$\" + $rutaServidor.Substring(3) + "\SRODeploy_temp.zip"
    $zipRemotoPATH = "$rutaServidor\SRODeploy_temp.zip"

    Copy-Item -Path $zipTemp -Destination $zipRemotoUNC -Force
    Write-OK "ZIP transferido"

    Write-INFO "  Descomprimiendo en el servidor..."
    Invoke-Command -ComputerName $Servidor -Credential $cred -ScriptBlock {
        param($zipPath, $destino, $protegidos)
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        foreach ($entry in $zip.Entries) {
            if ($entry.Name -notin $protegidos) {
                $destFile = Join-Path $destino $entry.FullName
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destFile, $true)
            }
        }
        $zip.Dispose()
        Remove-Item $zipPath -Force
    } -ArgumentList $zipRemotoPATH, $rutaServidor, $archivosProtegidos

    # Limpiar ZIP local
    Remove-Item $zipTemp -Force -ErrorAction SilentlyContinue
    Write-OK "Descomprimido correctamente"
}

Write-INFO "  Configs protegidos (no tocados):"
foreach ($cfg in $archivosProtegidos) {
    $rutaCfg = "SRODeploy:" + $rutaServidor.Substring(2) + "\" + $cfg
    if (Test-Path $rutaCfg) { Write-INFO "    [intacto] $cfg" }
}

# ============================================================
# PASO 5: Iniciar el servicio
# ============================================================
Write-STEP "5" "Iniciando servicio '$nombreServicio'..."

if ($DryRun) {
    Write-WARN "  [DRY RUN] Se iniciaria '$nombreServicio'"
} else {
    try {
        $estadoFinal = Invoke-Command -ComputerName $Servidor -Credential $cred -ScriptBlock {
            param($displayName, $internalName)
            $s = Get-Service -Name $internalName -ErrorAction SilentlyContinue
            if (-not $s) { return "NotFound" }
            Start-Service -Name $s.Name
            Start-Sleep -Seconds 4
            $s.Refresh()
            return $s.Status.ToString()
        } -ArgumentList $nombreServicio, $nombreServicioInterno

        if ($estadoFinal -eq "Running") {
            Write-OK "Servicio iniciado -- estado: Running"
        } elseif ($estadoFinal -eq "NotFound") {
            Write-WARN "Servicio no encontrado para iniciar"
        } else {
            Write-WARN "Estado inesperado: $estadoFinal"
        }
    } catch {
        Write-ERR "No se pudo iniciar el servicio: $($_.Exception.Message)"
    }
}

# ============================================================
# Limpieza + resumen
# ============================================================
Remove-PSDrive -Name "SRODeploy" -Force -ErrorAction SilentlyContinue

$duracion    = [math]::Round(((Get-Date) - $tiempoInicio).TotalSeconds, 1)
$versionNueva = (Get-Item "$releaseCore\ServicioReportesOracle.exe").VersionInfo.FileVersion

Write-Header "RESUMEN DEPLOY"
Write-INFO "  Servidor         : $Servidor"
Write-INFO "  Destino          : $rutaServidor"
Write-INFO "  Version anterior : $versionAntes"
Write-INFO "  Version nueva    : $versionNueva"
Write-INFO "  Archivos core    : $copiadosCore"
Write-INFO "  Archivos UI      : $copiadosUI"
Write-INFO "  Tiempo total     : ${duracion}s"
if ($DryRun) { Write-WARN "  MODO DRY RUN -- ningun cambio fue aplicado" }
else         { Write-OK   "  Deploy completado" }
Write-Host ""