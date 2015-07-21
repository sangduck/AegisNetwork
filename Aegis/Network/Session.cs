﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using Aegis;



namespace Aegis.Network
{
    public class Session
    {
        private static Int32 NextSessionId = 0;

        public Int32 SessionId { get; private set; }
        public Socket Socket { get; internal set; }
        public byte[] ReceivedBuffer { get; private set; }
        public Boolean IsConnected { get { return (Socket == null ? false : Socket.Connected); } }

        internal Action<Session> OnSessionClosed;
        private AsyncCallback _acReceive;
        private Int32 _receivedBytes;





        public Session(Int32 recvBufferSize)
        {
            Interlocked.Increment(ref NextSessionId);

            SessionId = NextSessionId;
            _acReceive = new AsyncCallback(OnRead);
            ReceivedBuffer = new byte[recvBufferSize];

            Clear();
        }


        internal void Clear()
        {
            if (Socket == null)
                return;

            Socket.Dispose();
            Socket = null;

            _receivedBytes = 0;
            Array.Clear(ReceivedBuffer, 0, ReceivedBuffer.Length);
        }


        internal void OnSocket_Accepted()
        {
            try
            {
                lock (this)
                    OnAccept();

                WaitForReceive();
            }
            catch (Exception e)
            {
                Logger.Write(LogType.Err, 1, e.ToString());
            }
        }


        internal void OnSocket_Closed()
        {
            try
            {
                lock (this)
                {
                    OnClose();
                    Clear();
                }
            }
            catch (Exception e)
            {
                Logger.Write(LogType.Err, 1, e.ToString());
            }


            OnSessionClosed(this);
        }


        private void WaitForReceive()
        {
            Int32 remainBufferSize = ReceivedBuffer.Length - _receivedBytes;
            Socket.BeginReceive(ReceivedBuffer, _receivedBytes, remainBufferSize, 0, _acReceive, null);
        }


        private void OnRead(IAsyncResult ar)
        {
            try
            {
                Int32 transBytes = Socket.EndReceive(ar);


                //  transBytes가 0이면 원격지 혹은 네트워크에 의해 연결이 끊긴 상태
                if (transBytes == 0)
                {
                    OnSocket_Closed();
                    return;
                }

                _receivedBytes += transBytes;


                //  패킷 하나가 정상적으로 수신되었는지 확인
                Int32 realPacketSize;
                if (IsValidPacket(transBytes, _receivedBytes - transBytes, out realPacketSize) == true)
                {
                    try
                    {
                        //  수신 이벤트
                        lock (this)
                        {
                            if (Socket != null)
                                OnReceive(realPacketSize);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Write(LogType.Err, 1, e.ToString());
                    }


                    //  패킷을 버퍼에서 제거
                    Array.Copy(ReceivedBuffer, _receivedBytes, ReceivedBuffer, 0, ReceivedBuffer.Length - realPacketSize);
                    _receivedBytes -= realPacketSize;
                }

                WaitForReceive();
            }
            catch (Exception e)
            {
                Logger.Write(LogType.Err, 1, e.ToString());
            }
        }


        protected virtual Boolean IsValidPacket(Int32 recvBytes, Int32 headerIndex, out Int32 realPacketSize)
        {
            realPacketSize = BitConverter.ToInt16(ReceivedBuffer, headerIndex);
            return (realPacketSize > 0 && _receivedBytes >= realPacketSize);
        }


        protected virtual void OnAccept()
        {
        }


        protected virtual void OnConnect(Boolean connected)
        {
        }


        protected virtual void OnClose()
        {
        }


        protected virtual void OnSend(Int32 transBytes)
        {
        }


        protected virtual void OnReceive(Int32 receivedPacketSize)
        {
        }
    }
}
