using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ServicioOracleReportes
{
    public class SoapClient
    {
        private readonly HttpClient _http;
        private readonly string _dominio;
        private readonly string _urlAuth;
        private readonly string _urlWs;

        public SoapClient(string dominio, string urlAuth, string urlWs)
        {
            _dominio = dominio;
            _urlAuth = urlAuth;
            _urlWs = urlWs;
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        public async Task<string> LoginAsync()
        {
            string envelope = 
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
                "xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<soap:Body><LoginServiceWithPackDirect xmlns=\"DkMServer.Services\">" +
                "<packName>dkactas</packName>" +
                $"<domain>{_dominio}</domain>" +
                $"<userName>{_dominio}</userName>" +
                $"<userPwd>{_dominio}</userPwd>" +
                "</LoginServiceWithPackDirect></soap:Body></soap:Envelope>";

            var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", "\"DkMServer.Services/LoginServiceWithPackDirect\"");
            
            var response = await _http.PostAsync(_urlAuth, content);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Login HTTP Error {(int)response.StatusCode}: {body}");

            string token = GetXmlVal(body, "UserToken");
            if (string.IsNullOrEmpty(token))
                throw new Exception($"No se pudo obtener el token de sesión. Respuesta del servidor:\n{body}");

            return token;
        }

        public async Task<string> ObtenerRegistrosGenericoAsync(string token, string entidad, string filtro)
        {
            string envelope =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
                "xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<soap:Body><ObtenerRegistrosGenerico xmlns=\"http://tempuri.org/\">" +
                $"<token>{token}</token>" +
                $"<entidad>{entidad}</entidad>" +
                "<condicionesFiltro>" +
                $"<string>{EscapeXml(filtro)}</string>" +
                "</condicionesFiltro>" +
                "</ObtenerRegistrosGenerico></soap:Body></soap:Envelope>";

            var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", "\"http://tempuri.org/ObtenerRegistrosGenerico\"");

            var response = await _http.PostAsync(_urlWs, content);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"WS HTTP Error {(int)response.StatusCode}: {body}");

            string codigo = GetXmlVal(body, "CodigoError");
            if (codigo != "0")
                throw new Exception($"Error SOAP {codigo}: {GetXmlVal(body, "ResultXML")}");

            return UnescapeXml(GetXmlVal(body, "ResultXML"));
        }

        private string GetXmlVal(string xml, string tag)
        {
            if (string.IsNullOrEmpty(xml)) return "";
            string open = $"<{tag}>";
            int s = xml.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (s < 0) return "";
            s += open.Length;
            int e = xml.IndexOf($"</{tag}>", s, StringComparison.OrdinalIgnoreCase);
            if (e < 0) return "";
            return xml.Substring(s, e - s).Trim();
        }

        private string UnescapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("&lt;", "<").Replace("&gt;", ">")
                    .Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&apos;", "'");
        }

        private string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }
}
