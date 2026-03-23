using System;
using System.Data;
using Oracle.ManagedDataAccess.Client;

namespace ServicioReportesOracle.UI.Services
{
    public class SqlService
    {
        private readonly string _connectionString;

        public SqlService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public DataTable ExecuteQuery(string sql)
        {
            DataTable dt = new DataTable();
            using (OracleConnection conn = new OracleConnection(_connectionString))
            {
                conn.Open();
                using (OracleCommand cmd = new OracleCommand(sql, conn))
                {
                    using (OracleDataAdapter da = new OracleDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                }
            }
            return dt;
        }

        public bool TestConnection()
        {
            try
            {
                using (OracleConnection conn = new OracleConnection(_connectionString))
                {
                    conn.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
