using System;
using System.IO;
using Newtonsoft.Json;
using ServicioReportesOracle.UI.Models;

namespace ServicioReportesOracle.UI.Helpers
{
    public class UiSettingsService
    {
        private static string GetPath()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(basePath, "..\\ServicioReportesOracle\\ui_settings.json");
        }

        public UiSettingsModel Load()
        {
            string path = GetPath();
            if (!File.Exists(path)) return new UiSettingsModel();
            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<UiSettingsModel>(json) ?? new UiSettingsModel();
            }
            catch
            {
                return new UiSettingsModel();
            }
        }

        public void Save(UiSettingsModel model)
        {
            string path = GetPath();
            var json = JsonConvert.SerializeObject(model, Formatting.Indented);
            File.WriteAllText(path, json);
        }
    }
}
