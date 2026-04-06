using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ServicioReportesOracle.UI
{
    public partial class AnaliticaWindow : Window
    {
        // ── Mock data ──────────────────────────────────────────────────────────

        private const double MockSuccessRate = 94.2;

        private readonly double[] _trend7d = { 142, 158, 165, 178, 145, 191, 203 };
        private readonly string[] _labels7d = { "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom" };

        private readonly double[] _trend30d =
        {
            95, 102, 88, 115, 128, 110, 132, 141, 138, 152,
            145, 163, 170, 158, 175, 182, 168, 191, 185, 203,
            178, 195, 212, 199, 208, 220, 215, 231, 218, 203
        };

        private double[]  _trend90d;
        private string[]  _labels90d;

        private readonly double[] _sparkData = { 2.8, 2.5, 3.1, 2.4, 2.7, 2.2, 2.3 };

        private readonly string[] _barLabels  = { "Sem -3", "Sem -2", "Sem -1", "Esta sem" };
        private readonly double[] _barOracle  = { 310, 345, 328, 352 };
        private readonly double[] _barSql     = { 148, 162, 155, 171 };

        // Colores reutilizables
        private static readonly Color CBlue    = Color.FromRgb(0x3B, 0x82, 0xF6);
        private static readonly Color CBlueFill = Color.FromArgb(25,  0x3B, 0x82, 0xF6);
        private static readonly Color CPurple  = Color.FromRgb(0xA8, 0x55, 0xF7);
        private static readonly Color CGreen   = Color.FromRgb(0x10, 0xB9, 0x81);
        private static readonly Color CGray    = Color.FromRgb(0xE5, 0xE7, 0xEB);
        private static readonly Color CGridLine = Color.FromRgb(0xF3, 0xF4, 0xF6);
        private static readonly Color CAxisText = Color.FromRgb(0x9C, 0xA3, 0xAF);
        private static readonly Color CText    = Color.FromRgb(0x1F, 0x29, 0x37);

        private double[] _currentTrend;
        private string[] _currentLabels;

        // ── Constructor ───────────────────────────────────────────────────────

        public AnaliticaWindow()
        {
            InitializeComponent();

            // Generar datos 90 días
            var rng = new Random(42);
            _trend90d = new double[90];
            _labels90d = new string[90];
            double v = 150;
            for (int i = 0; i < 90; i++)
            {
                v += (rng.NextDouble() - 0.45) * 15;
                v = Math.Max(50, Math.Min(350, v));
                _trend90d[i] = Math.Round(v);
                _labels90d[i] = $"D-{89 - i}";
            }

            _currentTrend  = _trend7d;
            _currentLabels = _labels7d;

            LoadMockGrid();

            // SizeChanged para canvases que se estiran con el layout
            SparklineCanvas.SizeChanged  += (s, e) => DrawSparkline(SparklineCanvas, _sparkData, CBlue);
            LineChartCanvas.SizeChanged  += (s, e) => DrawLineChart(LineChartCanvas, _currentTrend, _currentLabels);
            BarChartCanvas.SizeChanged   += (s, e) => DrawBarChart(BarChartCanvas);

            // Canvases con tamaño fijo explícito en XAML → dibujamos en Loaded
            Loaded += AnaliticaWindow_Loaded;
        }

        private void AnaliticaWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DrawGauge(GaugeCanvas, MockSuccessRate);
            DrawDonut(DonutCanvas);
        }

        // ── Gauge semicircular ────────────────────────────────────────────────

        private void DrawGauge(Canvas canvas, double value)
        {
            canvas.Children.Clear();

            double cx = canvas.Width  / 2.0;
            double cy = canvas.Height * 0.87;
            double outerR = Math.Min(canvas.Width * 0.46, canvas.Height * 0.85);
            double innerR = outerR * 0.67;

            double fraction = Math.Min(1.0, Math.Max(0.0, value / 100.0));

            // Arco de fondo (gris, 180°)
            AddGaugeRing(canvas, cx, cy, outerR, innerR, 0, 1, CGray);

            // Arco de valor
            if (fraction > 0.001)
            {
                Color c = fraction >= 0.9 ? CGreen
                        : fraction >= 0.7 ? CBlue
                        : Color.FromRgb(0xEF, 0x44, 0x44);
                AddGaugeRing(canvas, cx, cy, outerR, innerR, 0, fraction, c);
            }

            // Etiqueta del valor centrada
            var tbValue = new TextBlock
            {
                Text       = $"{value:F1}%",
                FontSize   = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(CText)
            };
            tbValue.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tbValue, cx - tbValue.DesiredSize.Width / 2);
            Canvas.SetTop(tbValue,  cy - 22);
            canvas.Children.Add(tbValue);

            // Etiqueta de calificación
            string qual = fraction >= 0.9 ? "Excelente" : fraction >= 0.7 ? "Normal" : "Atención";
            var tbQual = new TextBlock
            {
                Text       = qual,
                FontSize   = 11,
                Foreground = new SolidColorBrush(CAxisText)
            };
            tbQual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tbQual, cx - tbQual.DesiredSize.Width / 2);
            Canvas.SetTop(tbQual,  cy - 2);
            canvas.Children.Add(tbQual);

            // Etiqueta mín/máx
            AddLabel(canvas, "0%",    cx - outerR - 2, cy + 4, 10, CAxisText, HorizontalAlignment.Right);
            AddLabel(canvas, "100%",  cx + outerR + 2, cy + 4, 10, CAxisText, HorizontalAlignment.Left);
        }

        /// <summary>Dibuja un anillo de arco (forma de rosquilla semicircular).
        /// Divide recursivamente para evitar el bug de WPF con arcos de exactamente 180°.</summary>
        private void AddGaugeRing(Canvas canvas, double cx, double cy,
                                   double outerR, double innerR,
                                   double startFrac, double endFrac, Color color)
        {
            if (endFrac - startFrac <= 0.001) return;

            // Dividir arcos > 50% del gauge para evitar el bug WPF de 180°
            if (endFrac - startFrac > 0.5001)
            {
                double mid = (startFrac + endFrac) / 2.0;
                AddGaugeRing(canvas, cx, cy, outerR, innerR, startFrac, mid, color);
                AddGaugeRing(canvas, cx, cy, outerR, innerR, mid, endFrac, color);
                return;
            }

            // frac=0 → izquierda (ángulo math π), frac=1 → derecha (ángulo math 0)
            double startRad = (1.0 - startFrac) * Math.PI;
            double endRad   = (1.0 - endFrac)   * Math.PI;

            Point os = Pt(cx, cy, outerR, startRad);
            Point oe = Pt(cx, cy, outerR, endRad);
            Point is_ = Pt(cx, cy, innerR, startRad);
            Point ie = Pt(cx, cy, innerR, endRad);

            bool isLarge = (endFrac - startFrac) * 180.0 > 180.0;

            var fig = new PathFigure { StartPoint = os, IsClosed = true };
            // Arco exterior: izq → der pasando por la cima (Clockwise en pantalla)
            fig.Segments.Add(new ArcSegment(oe, new Size(outerR, outerR), 0,
                                             isLarge, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(ie, false));
            // Arco interior: der → izq (inverso)
            fig.Segments.Add(new ArcSegment(is_, new Size(innerR, innerR), 0,
                                             isLarge, SweepDirection.Counterclockwise, true));

            canvas.Children.Add(new Path
            {
                Data = new PathGeometry(new[] { fig }),
                Fill = new SolidColorBrush(color)
            });
        }

        // ── Donut chart ───────────────────────────────────────────────────────

        private void DrawDonut(Canvas canvas)
        {
            canvas.Children.Clear();

            double cx = canvas.Width  / 2.0;
            double cy = canvas.Height / 2.0;
            double R  = Math.Min(cx, cy) * 0.90;
            double ri = R * 0.56;

            // Oracle 68%
            AddPieSlice(canvas, cx, cy, R, 0,     244.8, CBlue);
            // SQL Server 32%
            AddPieSlice(canvas, cx, cy, R, 244.8, 115.2, CPurple);

            // Hueco interior (efecto donut)
            var hole = new Ellipse
            {
                Width  = ri * 2,
                Height = ri * 2,
                Fill   = Brushes.White
            };
            Canvas.SetLeft(hole, cx - ri);
            Canvas.SetTop(hole,  cy - ri);
            canvas.Children.Add(hole);

            // Etiqueta central
            var tb = new TextBlock
            {
                Text       = "1,847",
                FontSize   = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(CText)
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, cx - tb.DesiredSize.Width  / 2);
            Canvas.SetTop(tb,  cy - tb.DesiredSize.Height / 2);
            canvas.Children.Add(tb);
        }

        /// <summary>Dibuja un sector circular usando la convención "0° = cima, sentido horario".</summary>
        private void AddPieSlice(Canvas canvas, double cx, double cy, double R,
                                  double startDeg, double sweepDeg, Color color)
        {
            if (sweepDeg <= 0.001) return;

            double sRad = startDeg * Math.PI / 180.0;
            double eRad = (startDeg + sweepDeg) * Math.PI / 180.0;

            // En convención "cima=0, horario": P(θ) = (cx + R·sin(θ), cy - R·cos(θ))
            Point startPt = new Point(cx + R * Math.Sin(sRad), cy - R * Math.Cos(sRad));
            Point endPt   = new Point(cx + R * Math.Sin(eRad), cy - R * Math.Cos(eRad));

            bool isLarge = sweepDeg > 180;

            var fig = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
            fig.Segments.Add(new LineSegment(startPt, false));
            fig.Segments.Add(new ArcSegment(endPt, new Size(R, R), 0,
                                             isLarge, SweepDirection.Clockwise, true));

            canvas.Children.Add(new Path
            {
                Data = new PathGeometry(new[] { fig }),
                Fill = new SolidColorBrush(color)
            });
        }

        // ── Sparkline ─────────────────────────────────────────────────────────

        private void DrawSparkline(Canvas canvas, double[] data, Color lineColor)
        {
            canvas.Children.Clear();
            if (data == null || data.Length < 2) return;

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double min = double.MaxValue, max = double.MinValue;
            foreach (var d in data) { if (d < min) min = d; if (d > max) max = d; }
            double range = Math.Max(max - min, 0.001);

            double padV = h * 0.12;
            var pts = new PointCollection();
            for (int i = 0; i < data.Length; i++)
            {
                double x = w * i / (data.Length - 1);
                double y = padV + (h - 2 * padV) * (1 - (data[i] - min) / range);
                pts.Add(new Point(x, y));
            }

            // Área rellena
            var areaPts = new PointCollection(pts);
            areaPts.Add(new Point(w, h));
            areaPts.Add(new Point(0, h));
            canvas.Children.Add(new Polygon
            {
                Points    = areaPts,
                Fill      = new SolidColorBrush(Color.FromArgb(30, lineColor.R, lineColor.G, lineColor.B)),
                Stroke    = null
            });

            // Línea
            canvas.Children.Add(new Polyline
            {
                Points          = pts,
                Stroke          = new SolidColorBrush(lineColor),
                StrokeThickness = 2,
                StrokeLineJoin  = PenLineJoin.Round
            });

            // Punto final
            var last = pts[pts.Count - 1];
            var dot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(lineColor) };
            Canvas.SetLeft(dot, last.X - 3);
            Canvas.SetTop(dot,  last.Y - 3);
            canvas.Children.Add(dot);
        }

        // ── Line Chart ────────────────────────────────────────────────────────

        private void DrawLineChart(Canvas canvas, double[] data, string[] labels)
        {
            canvas.Children.Clear();
            if (data == null || data.Length < 2) return;

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            const double padL = 46, padR = 14, padT = 16, padB = 30;
            double cw = w - padL - padR;
            double ch = h - padT - padB;

            // Escala Y
            double maxY = 0;
            foreach (var d in data) if (d > maxY) maxY = d;
            maxY = Math.Ceiling(maxY / 50.0) * 50;
            double range = Math.Max(maxY, 1);

            // Grid horizontal + etiquetas Y
            int gridCount = 5;
            for (int i = 0; i <= gridCount; i++)
            {
                double gy  = padT + ch * (1.0 - (double)i / gridCount);
                double val = range * i / gridCount;

                canvas.Children.Add(new Line
                {
                    X1 = padL, Y1 = gy, X2 = padL + cw, Y2 = gy,
                    Stroke = new SolidColorBrush(i == 0 ? Color.FromRgb(0xD1, 0xD5, 0xDB) : CGridLine),
                    StrokeThickness = 1
                });

                var lbl = MakeText($"{val:F0}", 10, CAxisText);
                lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(lbl, padL - lbl.DesiredSize.Width - 5);
                Canvas.SetTop(lbl,  gy - lbl.DesiredSize.Height / 2);
                canvas.Children.Add(lbl);
            }

            // Eje X inferior
            canvas.Children.Add(new Line
            {
                X1 = padL, Y1 = padT + ch, X2 = padL + cw, Y2 = padT + ch,
                Stroke = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
                StrokeThickness = 1
            });

            // Puntos del gráfico
            int n = data.Length;
            var pts = new PointCollection();
            for (int i = 0; i < n; i++)
            {
                double x = padL + cw * i / Math.Max(n - 1, 1);
                double y = padT + ch * (1.0 - data[i] / range);
                pts.Add(new Point(x, y));
            }

            // Área bajo la línea
            var areaPts = new PointCollection(pts);
            areaPts.Add(new Point(padL + cw, padT + ch));
            areaPts.Add(new Point(padL,      padT + ch));
            canvas.Children.Add(new Polygon
            {
                Points    = areaPts,
                Fill      = new SolidColorBrush(CBlueFill),
                Stroke    = null
            });

            // Línea principal
            canvas.Children.Add(new Polyline
            {
                Points          = pts,
                Stroke          = new SolidColorBrush(CBlue),
                StrokeThickness = 2.5,
                StrokeLineJoin  = PenLineJoin.Round
            });

            // Puntos interactivos + etiquetas X (solo cada N para no saturar)
            int step = n <= 10 ? 1 : n <= 31 ? 5 : 10;
            for (int i = 0; i < n; i++)
            {
                // Punto
                var dot = new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(CBlue) };
                Canvas.SetLeft(dot, pts[i].X - 2.5);
                Canvas.SetTop(dot,  pts[i].Y - 2.5);
                canvas.Children.Add(dot);

                // Etiqueta X
                if (i % step == 0 && i < labels.Length)
                {
                    var xl = MakeText(labels[i], 9, CAxisText);
                    xl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(xl, pts[i].X - xl.DesiredSize.Width / 2);
                    Canvas.SetTop(xl,  padT + ch + 5);
                    canvas.Children.Add(xl);
                }
            }
        }

        // ── Bar Chart ─────────────────────────────────────────────────────────

        private void DrawBarChart(Canvas canvas)
        {
            canvas.Children.Clear();

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            const double padL = 50, padR = 14, padT = 16, padB = 30;
            double cw = w - padL - padR;
            double ch = h - padT - padB;

            double maxY = 0;
            for (int i = 0; i < _barOracle.Length; i++)
            {
                if (_barOracle[i] > maxY) maxY = _barOracle[i];
                if (_barSql[i]    > maxY) maxY = _barSql[i];
            }
            maxY = Math.Ceiling(maxY / 100.0) * 100;
            double range = Math.Max(maxY, 1);

            // Gridlines horizontales
            int gridCount = 4;
            for (int i = 0; i <= gridCount; i++)
            {
                double gy  = padT + ch * (1.0 - (double)i / gridCount);
                double val = range * i / gridCount;

                canvas.Children.Add(new Line
                {
                    X1 = padL, Y1 = gy, X2 = padL + cw, Y2 = gy,
                    Stroke = new SolidColorBrush(i == 0 ? Color.FromRgb(0xD1, 0xD5, 0xDB) : CGridLine),
                    StrokeThickness = 1
                });

                var lbl = MakeText($"{val:F0}", 10, CAxisText);
                lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(lbl, padL - lbl.DesiredSize.Width - 5);
                Canvas.SetTop(lbl,  gy - lbl.DesiredSize.Height / 2);
                canvas.Children.Add(lbl);
            }

            // Barras agrupadas
            int groups   = _barLabels.Length;
            double groupW = cw / groups;
            double barW   = groupW * 0.30;
            double gap    = groupW * 0.06;

            for (int g = 0; g < groups; g++)
            {
                double groupX = padL + groupW * g + groupW * 0.10;

                // Oracle
                double hOracle = ch * _barOracle[g] / range;
                var rOracle = new Rectangle
                {
                    Width   = barW,
                    Height  = hOracle,
                    Fill    = new SolidColorBrush(CBlue),
                    RadiusX = 3, RadiusY = 3
                };
                Canvas.SetLeft(rOracle, groupX);
                Canvas.SetTop(rOracle,  padT + ch - hOracle);
                canvas.Children.Add(rOracle);

                // SQL Server
                double hSql = ch * _barSql[g] / range;
                var rSql = new Rectangle
                {
                    Width   = barW,
                    Height  = hSql,
                    Fill    = new SolidColorBrush(CPurple),
                    RadiusX = 3, RadiusY = 3
                };
                Canvas.SetLeft(rSql, groupX + barW + gap);
                Canvas.SetTop(rSql,  padT + ch - hSql);
                canvas.Children.Add(rSql);

                // Etiqueta del grupo
                var xl = MakeText(_barLabels[g], 9, CAxisText);
                xl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double centerX = groupX + barW + gap / 2;
                Canvas.SetLeft(xl, centerX - xl.DesiredSize.Width / 2);
                Canvas.SetTop(xl,  padT + ch + 5);
                canvas.Children.Add(xl);
            }
        }

        // ── DataGrid mock data ────────────────────────────────────────────────

        private void LoadMockGrid()
        {
            var rows = new List<EjecucionRow>();
            var rng  = new Random(7);
            var tareas = new[]
            {
                "RPT_VENTAS_DIARIAS", "RPT_STOCK_ALMACEN", "RPT_FACTURAS_PENDIENTES",
                "RPT_CLIENTES_ACTIVOS", "RPT_ORDENES_COMPRA", "RPT_DESPACHOS_DIA",
                "RPT_CONCILIACION_CTAS", "RPT_MLOGIS_SYNC", "RPT_ALERTAS_SOAP"
            };
            var motores = new[] { "Oracle", "Oracle", "Oracle", "SQL Server" };
            var estados = new[] { "✅ OK", "✅ OK", "✅ OK", "✅ OK", "⚠️ Advertencia", "❌ Error" };

            var now = DateTime.Now;
            for (int i = 0; i < 20; i++)
            {
                var t = now.AddMinutes(-(i * rng.Next(15, 90)));
                rows.Add(new EjecucionRow
                {
                    FechaHora = t.ToString("dd/MM/yyyy HH:mm:ss"),
                    Tarea     = tareas[rng.Next(tareas.Length)],
                    Motor     = motores[rng.Next(motores.Length)],
                    Duracion  = $"{(rng.NextDouble() * 5 + 0.3):F2}s",
                    Filas     = rng.Next(50, 5000).ToString("N0"),
                    Estado    = estados[rng.Next(estados.Length)]
                });
            }

            EjecucionesGrid.ItemsSource = rows;
        }

        // ── Eventos de UI ─────────────────────────────────────────────────────

        private void PeriodoCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LineChartCanvas == null) return;

            switch (PeriodoCombo.SelectedIndex)
            {
                case 0: _currentTrend = _trend7d;  _currentLabels = _labels7d;  break;
                case 1: _currentTrend = _trend30d; _currentLabels = BuildLabels(30); break;
                default: _currentTrend = _trend90d; _currentLabels = _labels90d; break;
            }

            DrawLineChart(LineChartCanvas, _currentTrend, _currentLabels);
        }

        private void AnalizarButton_Click(object sender, RoutedEventArgs e)
        {
            string query = QueryBox.Text?.Trim();
            if (string.IsNullOrEmpty(query)) return;
            // TODO: conectar con DataEngine / JSONs de Logs\json\
            MessageBox.Show($"Consulta recibida:\n\"{query}\"\n\nIntegración con DataEngine pendiente.",
                            "SRO Analytics", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            // TODO: exportar EjecucionesGrid a Excel con ClosedXML
            MessageBox.Show("Exportación a Excel disponible en la próxima iteración\n" +
                            "(requiere integración con datos reales de Logs\\json\\).",
                            "Exportar Excel", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Punto en coordenadas de pantalla para ángulo matemático (Y invertido).</summary>
        private static Point Pt(double cx, double cy, double r, double angle) =>
            new Point(cx + r * Math.Cos(angle), cy - r * Math.Sin(angle));

        private static TextBlock MakeText(string text, double fontSize, Color color,
                                           FontWeight? weight = null) =>
            new TextBlock
            {
                Text       = text,
                FontSize   = fontSize,
                FontWeight = weight ?? FontWeights.Normal,
                Foreground = new SolidColorBrush(color)
            };

        private static void AddLabel(Canvas canvas, string text, double x, double y,
                                      double fontSize, Color color, HorizontalAlignment align)
        {
            var tb = MakeText(text, fontSize, color);
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double left = align == HorizontalAlignment.Right
                ? x - tb.DesiredSize.Width
                : x;
            Canvas.SetLeft(tb, left);
            Canvas.SetTop(tb, y);
            canvas.Children.Add(tb);
        }

        private static string[] BuildLabels(int n)
        {
            var result = new string[n];
            for (int i = 0; i < n; i++) result[i] = $"D-{n - 1 - i}";
            return result;
        }
    }

    // ── Modelo de fila del DataGrid ───────────────────────────────────────────

    internal sealed class EjecucionRow
    {
        public string FechaHora { get; set; }
        public string Tarea     { get; set; }
        public string Motor     { get; set; }
        public string Duracion  { get; set; }
        public string Filas     { get; set; }
        public string Estado    { get; set; }
    }
}
