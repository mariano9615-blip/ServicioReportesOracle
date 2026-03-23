using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using ServicioReportesOracle.UI.Models;

namespace ServicioReportesOracle.UI.Services
{
    public class ConfigService
    {
        private readonly string _configPath;
        private readonly string _consultasPath;

        public ConfigService(string configPath, string consultasPath)
        {
            _configPath = configPath;
            _consultasPath = consultasPath;
        }

        public ConfigModel LoadConfig()
        {
            if (!File.Exists(_configPath)) return new ConfigModel();
            var json = File.ReadAllText(_configPath);
            return JsonConvert.DeserializeObject<ConfigModel>(json);
        }

        public void SaveConfig(ConfigModel config)
        {
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(_configPath, json);
        }

        public List<ConsultaTaskModel> LoadConsultas()
        {
            if (!File.Exists(_consultasPath)) return new List<ConsultaTaskModel>();
            var json = File.ReadAllText(_consultasPath);
            return JsonConvert.DeserializeObject<List<ConsultaTaskModel>>(json);
        }

        public void SaveConsultas(List<ConsultaTaskModel> consultas)
        {
            var json = JsonConvert.SerializeObject(consultas, Formatting.Indented);
            File.WriteAllText(_consultasPath, json);
        }
    }
}
