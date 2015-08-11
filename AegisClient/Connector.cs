﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using Aegis;



namespace Aegis.Client
{
    internal class Connector
    {
        private AegisClient _aegisClient;
        private Socket _socket;
        private StreamBuffer _receivedBuffer;

        public Boolean IsConnected { get { return (_socket == null ? false : _socket.Connected); } }





        public Connector(AegisClient parent)
        {
            _aegisClient = parent;
            _receivedBuffer = new StreamBuffer();
        }


        public Boolean Connect(String ipAddress, Int32 portNo)
        {
            if (_socket != null)
                throw new AegisException("This session has already been activated.");


            //  연결 시도
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), portNo);
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.Connect(ipEndPoint);


            if (_socket.Connected == false)
            {
                _socket.Close();
                _socket = null;
                return false;
            }

            WaitForReceive();
            return true;
        }


        public void Close()
        {
            try
            {
                if (_socket == null)
                    return;

                _receivedBuffer.Clear();
                _socket.Close();
                _socket = null;
            }
            catch (Exception)
            {
                //  Nothing to do
            }
        }


        private void WaitForReceive()
        {
            if (_receivedBuffer.WritableSize == 0)
                throw new AegisException("There is no remaining capacity of the receive buffer.");

            if (_socket != null && _socket.Connected)
                _socket.BeginReceive(_receivedBuffer.Buffer, _receivedBuffer.WrittenBytes, _receivedBuffer.WritableSize, 0, OnSocket_Read, null);
        }


        private void OnSocket_Read(IAsyncResult ar)
        {
            try
            {
                //  transBytes가 0이면 원격지 혹은 네트워크에 의해 연결이 끊긴 상태
                Int32 transBytes = _socket.EndReceive(ar);
                if (transBytes == 0)
                {
                    _aegisClient.MQ.Add(MessageType.Disconnect, null, 0);
                    return;
                }


                _receivedBuffer.Write(transBytes);
                while (_receivedBuffer.ReadableSize > 0)
                {
                    //  패킷 하나가 정상적으로 수신되었는지 확인
                    Int32 packetSize = 0;
                    if (_aegisClient.IsValidPacket(_receivedBuffer, out packetSize) == false)
                        break;


                    //  수신처리
                    try
                    {
                        StreamBuffer dispatchBuffer = new StreamBuffer(_receivedBuffer.Buffer, 0, packetSize);
                        _aegisClient.MQ.Add(MessageType.Receive, dispatchBuffer, dispatchBuffer.WrittenBytes);


                        _receivedBuffer.ResetReadIndex();
                        _receivedBuffer.Read(packetSize);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e.ToString());
                    }


                    //  처리된 패킷을 버퍼에서 제거
                    _receivedBuffer.PopReadBuffer();
                }


                //  ReceiveBuffer의 안정적인 처리를 위해 OnReceive 작업이 끝난 후에 다시 수신대기
                WaitForReceive();
            }
            catch (SocketException)
            {
                _aegisClient.MQ.Add(MessageType.Close, null, 0);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
            }
        }


        public Boolean SendPacket(StreamBuffer source)
        {
            try
            {
                _socket.Send(source.Buffer, 0, source.WrittenBytes, SocketFlags.None);
                return true;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
            }

            return false;
        }
    }
}
