﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Aegis;



namespace Aegis.Network
{
    [DebuggerDisplay("Name={Name}")]
    public class NetworkChannel
    {
        /// <summary>
        /// 이 NetworkChannel 객체의 고유한 이름입니다.
        /// </summary>
        public String Name { get; private set; }
        /// <summary>
        /// 이 NetworkChannel에서 사용하는 Session 객체를 관리합니다.
        /// </summary>
        internal Acceptor Acceptor { get; private set; }
        public static List<NetworkChannel> Channels = new List<NetworkChannel>();
        public List<Session> ActiveSessions { get; } = new List<Session>();
        internal List<Session> InactiveSessions { get; } = new List<Session>();
        public Int32 MaxSessionCount { get; set; }
        private SessionGenerateDelegator _sessionGenerator;





        /// <summary>
        /// NetworkChannel 객체를 생성합니다.
        /// name은 이전에 생성된 NetworkChannel과 동일한 문자열을 사용할 수 없습니다.
        /// </summary>
        /// <param name="name">생성할 NetworkChannel의 고유한 이름.</param>
        /// <returns>생성된 NetworkChannel 객체</returns>
        public static NetworkChannel CreateChannel(String name)
        {
            lock (Channels)
            {
                NetworkChannel channel = Channels.Find(v => v.Name == name);
                if (channel != null)
                    throw new AegisException(AegisResult.AlreadyExistName, "Already exists same name.");


                channel = new NetworkChannel(name);
                Channels.Add(channel);

                return channel;
            }
        }


        /// <summary>
        /// 생성된 모든 NetworkChannel을 종료하고 사용중인 리소스를 반환합니다.
        /// 활성화된 Acceptor, Session 등 모든 네트워크 작업이 종료됩니다.
        /// </summary>
        public static void Release()
        {
            lock (Channels)
            {
                foreach (NetworkChannel networkChannel in Channels)
                    networkChannel.StopNetwork();

                Channels.Clear();
            }
        }


        /// <summary>
        /// name을 사용하여 NetworkChannel을 가져옵니다.
        /// </summary>
        /// <param name="name">검색할 NetworkChannel의 이름</param>
        /// <returns>검색된 NetworkChannel 객체</returns>
        public static NetworkChannel FindChannel(String name)
        {
            lock (Channels)
            {
                return Channels.Find(v => v.Name == name);
            }
        }


        private NetworkChannel(String name)
        {
            Name = name;
            Acceptor = new Acceptor(this);
        }


        internal Session GenerateSession()
        {
            lock (this)
            {
                if (InactiveSessions.Count == 0)
                {
                    if (MaxSessionCount == 0)
                    {
                        Session session = _sessionGenerator();
                        session.Activated += OnSessionActivated;
                        session.Inactivated += OnSessionInactivated;

                        InactiveSessions.Add(session);
                    }
                    else
                        return null;
                }

                return InactiveSessions[0];
            }
        }


        private void OnSessionActivated(Session session)
        {
            lock (this)
            {
                InactiveSessions.Remove(session);
                ActiveSessions.Add(session);
            }
        }


        private void OnSessionInactivated(Session session)
        {
            lock (this)
            {
                ActiveSessions.Remove(session);
                InactiveSessions.Add(session);
            }
        }


        /// <summary>
        /// 네트워크 작업을 시작합니다.
        /// </summary>
        /// <param name="generator">Session 객체를 생성하는 Delegator를 설정합니다. SessionManager에서는 내부적으로 Session Pool을 관리하는데, Pool에 객체가 부족할 때 이 Delegator가 호출됩니다. 그러므로 이 Delegator에서는 ObjectPool 대신 new를 사용해 인스턴스를 생성하는 것이 좋습니다.</param>
        /// <param name="initPoolCount">처음 생성할 Session의 개수를 지정합니다.</param>
        /// <param name="maxPoolCount">생성할 수 있는 Session의 최대개수를 지정합니다. 0이 입력되면 최대개수를 무시합니다.</param>
        /// <returns>현재 NetworkChannel 객체를 반환합니다.</returns>
        public NetworkChannel StartNetwork(SessionGenerateDelegator generator, Int32 initPoolCount, Int32 maxPoolCount)
        {
            if (generator == null)
                throw new AegisException(AegisResult.InvalidArgument, $"Argument '{nameof(generator)}' could not be null.");

            _sessionGenerator = generator;
            MaxSessionCount = maxPoolCount;


            lock (this)
            {
                while (initPoolCount <= maxPoolCount && initPoolCount-- > 0)
                {
                    Session session = _sessionGenerator();
                    session.Activated += OnSessionActivated;
                    session.Inactivated += OnSessionInactivated;

                    InactiveSessions.Add(session);
                }
            }


            return this;
        }


        /// <summary>
        /// 네트워크 작업을 종료하고 사용중인 리소스를 반환합니다.
        /// Acceptor와 활성화된 Session의 네트워크 작업이 중단됩니다.
        /// </summary>
        public void StopNetwork()
        {
            Acceptor.Close();


            List<Session> targets;
            lock (this)
            {
                targets = ActiveSessions.Concat(InactiveSessions).ToList();
            }
            targets.ForEach(v => v.Close());
        }


        /// <summary>
        /// 클라이언트의 연결요청을 받을 수 있도록 Listener를 오픈합니다.
        /// </summary>
        /// <param name="ipAddress">접속요청 받을 Ip Address</param>
        /// <param name="portNo">접속요청 받을 PortNo</param>
        /// <returns>현재 NetworkChannel 객체를 반환합니다.</returns>
        public NetworkChannel OpenListener(String ipAddress, Int32 portNo)
        {
            Acceptor.Listen(ipAddress, portNo);
            return this;
        }


        /// <summary>
        /// Listener를 종료합니다.
        /// </summary>
        public void CloseListener()
        {
            Acceptor.Close();
        }
    }
}
