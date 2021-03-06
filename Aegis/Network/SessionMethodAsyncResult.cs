﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using Aegis;
using Aegis.Threading;



namespace Aegis.Network
{
    internal class SessionMethodAsyncResult : ISessionMethod
    {
        private Session _session;
        private StreamBuffer _receivedBuffer, _dispatchBuffer;

        private ResponseSelector _responseSelector;





        public SessionMethodAsyncResult(Session session)
        {
            _session = session;
            _receivedBuffer = new StreamBuffer(2048);
            _dispatchBuffer = new StreamBuffer(2048);
            _responseSelector = new ResponseSelector(_session);
        }


        public void Clear()
        {
            _receivedBuffer.Clear();
            _dispatchBuffer.Clear();
        }


        public void WaitForReceive()
        {
            try
            {
                lock (_session)
                {
                    if (_session.Socket == null)
                        return;

                    if (_receivedBuffer.WritableSize == 0)
                        _receivedBuffer.Resize(_receivedBuffer.BufferSize * 2);

                    if (_session.Socket.Connected)
                        _session.Socket.BeginReceive(_receivedBuffer.Buffer, _receivedBuffer.WrittenBytes, _receivedBuffer.WritableSize, 0, OnSocket_Read, null);
                    else
                        _session.Close();
                }
            }
            catch (Exception)
            {
                _session.Close();
            }
        }


        private void OnSocket_Read(IAsyncResult ar)
        {
            try
            {
                lock (_session)
                {
                    if (_session.Socket == null)
                        return;

                    //  transBytes가 0이면 원격지 혹은 네트워크에 의해 연결이 끊긴 상태
                    Int32 transBytes = _session.Socket.EndReceive(ar);
                    if (transBytes == 0)
                    {
                        _session.Close();
                        return;
                    }


                    _receivedBuffer.Write(transBytes);
                    while (_receivedBuffer.ReadableSize > 0)
                    {
                        _dispatchBuffer.Clear();
                        _dispatchBuffer.Write(_receivedBuffer.Buffer, _receivedBuffer.ReadBytes, _receivedBuffer.ReadableSize);


                        //  패킷 하나가 정상적으로 수신되었는지 확인
                        Int32 packetSize;

                        _dispatchBuffer.ResetReadIndex();
                        if (_session.PacketValidator == null ||
                            _session.PacketValidator(_dispatchBuffer, out packetSize) == false)
                            break;

                        try
                        {
                            //  수신 이벤트 처리 중 종료 이벤트가 발생한 경우
                            if (_session.Socket == null)
                                return;


                            //  수신처리(Dispatch)
                            _receivedBuffer.Read(packetSize);
                            _dispatchBuffer.ResetReadIndex();


                            if (_responseSelector.Dispatch(_dispatchBuffer) == false)
                                _session.OnReceived(_dispatchBuffer);
                        }
                        catch (Exception e)
                        {
                            Logger.Write(LogType.Err, 1, e.ToString());
                        }
                    }


                    //  처리된 패킷을 버퍼에서 제거
                    _receivedBuffer.PopReadBuffer();

                    //  ReceiveBuffer의 안정적인 처리를 위해 작업이 끝난 후에 다시 수신대기
                    WaitForReceive();
                }
            }
            catch (SocketException)
            {
                _session.Close();
            }
            catch (Exception e)
            {
                Logger.Write(LogType.Err, 1, e.ToString());
            }
        }


        public void SendPacket(byte[] buffer, Int32 offset, Int32 size, Action<StreamBuffer> onSent = null)
        {
            try
            {
                lock (_session)
                {
                    if (_session.Socket != null)
                    {
                        if (onSent == null)
                            _session.Socket.BeginSend(buffer, offset, size, SocketFlags.None, OnSocket_Send, null);
                        else
                            _session.Socket.BeginSend(buffer, offset, size, SocketFlags.None, OnSocket_Send,
                                             new NetworkSendToken(new StreamBuffer(buffer, offset, size), onSent));
                    }
                }
            }
            catch (SocketException)
            {
            }
            catch (Exception e)
            {
                Logger.Write(LogType.Err, 1, e.ToString());
            }
        }


        public void SendPacket(StreamBuffer buffer, Action<StreamBuffer> onSent = null)
        {
            try
            {
                lock (_session)
                {
                    if (_session.Socket != null)
                    {
                        //  ReadIndex가 OnSocket_Send에서 사용되므로 ReadIndex를 초기화해야 한다.
                        buffer.ResetReadIndex();

                        if (onSent == null)
                            _session.Socket.BeginSend(buffer.Buffer, 0, buffer.WrittenBytes, SocketFlags.None, OnSocket_Send, null);
                        else
                            _session.Socket.BeginSend(buffer.Buffer, 0, buffer.WrittenBytes, SocketFlags.None, OnSocket_Send,
                                             new NetworkSendToken(buffer, onSent));
                    }
                }
            }
            catch (SocketException)
            {
            }
            catch (Exception e)
            {
                Logger.Write(LogType.Err, 1, e.ToString());
            }
        }


        public void SendPacket(StreamBuffer buffer, PacketCriterion criterion, EventHandler_Receive dispatcher, Action<StreamBuffer> onSent = null)
        {
            if (criterion == null || dispatcher == null)
                throw new AegisException(AegisResult.InvalidArgument, "The argument criterion and dispatcher cannot be null.");

            try
            {
                lock (_session)
                {
                    _responseSelector.Add(criterion, dispatcher);
                    if (_session.Socket != null)
                    {
                        //  ReadIndex가 OnSocket_Send에서 사용되므로 ReadIndex를 초기화해야 한다.
                        buffer.ResetReadIndex();

                        if (onSent == null)
                            _session.Socket.BeginSend(buffer.Buffer, 0, buffer.WrittenBytes, SocketFlags.None, OnSocket_Send, null);
                        else
                            _session.Socket.BeginSend(buffer.Buffer, 0, buffer.WrittenBytes, SocketFlags.None, OnSocket_Send,
                                             new NetworkSendToken(buffer, onSent));
                    }
                }
            }
            catch (SocketException)
            {
            }
            catch (Exception e)
            {
                Logger.Write(LogType.Err, 1, e.ToString());
            }
        }


        private void OnSocket_Send(IAsyncResult ar)
        {
            try
            {
                lock (_session)
                {
                    if (_session.Socket == null)
                        return;


                    Int32 transBytes = _session.Socket.EndSend(ar);
                    NetworkSendToken token = (NetworkSendToken)ar.AsyncState;

                    if (token != null)
                    {
                        token.Buffer.Read(transBytes);
                        if (token.Buffer.ReadableSize == 0)
                            token.CompletionAction();
                    }
                }
            }
            catch (SocketException)
            {
            }
            catch (Exception e)
            {
                Logger.Write(LogType.Err, 1, e.ToString());
            }
        }
    }
}
