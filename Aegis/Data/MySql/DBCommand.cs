﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;



namespace Aegis.Data.MySql
{
    public sealed class DBCommand : IDisposable
    {
        private MySqlDatabase _mysql;
        private MySqlCommand _cmd;
        private DataReader _reader;
        private DBConnector _dbConnector;
        private Boolean _isAsync;
        private List<Tuple<String, Object>> _prepareBindings;

        public StringBuilder CommandText { get; set; }
        public Int32 CommandTimeout { get { return _cmd.CommandTimeout; } set { _cmd.CommandTimeout = value; } }
        public Int64 LastInsertedId
        {
            get
            {
                if (_cmd == null)
                    return 0;

                return _cmd.LastInsertedId;
            }
        }





        private DBCommand()
        {
            CommandText = new StringBuilder(256);
            _prepareBindings = new List<Tuple<String, Object>>();
            _cmd = new MySqlCommand();
        }


        public static DBCommand NewCommand(MySqlDatabase mysql, Int32 timeoutSec = 60)
        {
            DBCommand obj = ObjectPool<DBCommand>.Pop();
            obj._mysql = mysql;
            obj._isAsync = false;
            obj.CommandTimeout = timeoutSec;

            return obj;
        }


        public void Dispose()
        {
            //  비동기로 동작중인 쿼리는 작업이 끝나기 전에 반환할 수 없다.
            if (_isAsync == true)
                return;


            EndQuery();

            _mysql = null;
            _cmd.Connection = null;
            ObjectPool<DBCommand>.Push(this);
        }


        public void QueryNoReader()
        {
            if (_dbConnector != null || _reader != null)
                throw new AegisException(AegisResult.DataReaderNotClosed, "There is already an open DataReader associated with this Connection which must be closed first.");


            _dbConnector = _mysql.GetDBC();
            _cmd.Connection = _dbConnector.Connection;
            _cmd.CommandText = CommandText.ToString();

            Prepare();
            _cmd.ExecuteNonQuery();
            _cmd.Connection = null;

            _dbConnector.IncreaseQueryCount();
            _dbConnector.Dispose();
            _dbConnector = null;
        }


        public DataReader Query()
        {
            if (_dbConnector != null || _reader != null)
                throw new AegisException(AegisResult.DataReaderNotClosed, "There is already an open DataReader associated with this Connection which must be closed first.");


            _dbConnector = _mysql.GetDBC();
            _cmd.Connection = _dbConnector.Connection;
            _cmd.CommandText = CommandText.ToString();

            Prepare();
            _reader = new DataReader(_cmd.ExecuteReader());
            _cmd.Connection = null;

            _dbConnector.IncreaseQueryCount();
            //  DataReader가 사용중이므로 _dbConnector를 유지해야 한다.

            return _reader;
        }


        public void QueryNoReader(String query, params object[] args)
        {
            CommandText.Clear();
            CommandText.AppendFormat(query, args);

            QueryNoReader();
        }


        public DataReader Query(String query, params object[] args)
        {
            CommandText.Clear();
            CommandText.AppendFormat(query, args);

            return Query();
        }


        public void PostQuery()
        {
            _isAsync = true;
            _mysql.WorkerQueue.Post(() =>
            {
                QueryNoReader();
                _isAsync = false;
                Dispose();
            });
        }


        public void PostQuery(Action<DataReader> postAction)
        {
            _isAsync = true;
            _mysql.WorkerQueue.Post(() =>
            {
                DataReader reader = Query();
                postAction(reader);

                _isAsync = false;
                Dispose();
            });
        }


        private void Prepare()
        {
            if (_prepareBindings.Count() == 0)
                return;

            _cmd.Prepare();
            foreach (Tuple<String, Object> param in _prepareBindings)
                _cmd.Parameters.AddWithValue(param.Item1, param.Item2);
        }


        public void BindParameter(String parameterName, object value)
        {
            _prepareBindings.Add(new Tuple<String, Object>(parameterName, value));
        }


        public void EndQuery()
        {
            CommandText.Clear();
            _prepareBindings.Clear();
            _cmd.Parameters.Clear();
            _cmd.Connection = null;

            if (_dbConnector != null)
            {
                _dbConnector.Dispose();
                _dbConnector = null;
            }

            if (_reader != null)
            {
                _reader.Dispose();
                _reader = null;
            }
        }
    }
}
