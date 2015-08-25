﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using Aegis;
using Aegis.Threading;



namespace Aegis.Network
{
    /// <summary>
    /// Async 계열의 Socket API를 사용하여 원격지의 호스트와 네트워킹을 할 수 있는 기능을 제공합니다.
    /// </summary>
    public class AsyncEventSession : SessionBase
    {
        private StreamBuffer _receivedBuffer, _dispatchBuffer;
        private SocketAsyncEventArgs _saeaRecv;
        private Queue<SocketAsyncEventArgs> _queueSaeaSend = new Queue<SocketAsyncEventArgs>();

        public AwaitableMethod AwaitableMethod { get; private set; }
        private ResponseAlternator _alternator;


        public event EventHandler_Send NetworkEvent_Sent;
        public event EventHandler_Receive NetworkEvent_Received;
        public EventHandler_IsValidPacket PacketValidator;





        /// <summary>
        /// 수신버퍼의 크기는 StreamBuffer의 기본할당크기로 초기화됩니다.
        /// </summary>
        protected AsyncEventSession()
        {
            _receivedBuffer = new StreamBuffer();
            _dispatchBuffer = new StreamBuffer();

            _saeaRecv = new SocketAsyncEventArgs();
            _saeaRecv.Completed += OnComplete_Receive;

            AwaitableMethod = new AwaitableMethod(this);
            _alternator = new ResponseAlternator(this);
        }


        /// <summary>
        /// 수신버퍼의 크기를 지정하여 SessionAsync 객체를 생성합니다. 수신버퍼의 크기는 패킷 하나의 크기 이상으로 설정하는 것이 좋습니다.
        /// </summary>
        /// <param name="recvBufferSize">수신버퍼의 크기(Byte)</param>
        protected AsyncEventSession(Int32 recvBufferSize)
        {
            _receivedBuffer = new StreamBuffer(recvBufferSize);
            _dispatchBuffer = new StreamBuffer();

            _saeaRecv = new SocketAsyncEventArgs();
            _saeaRecv.Completed += OnComplete_Receive;

            AwaitableMethod = new AwaitableMethod(this);
            _alternator = new ResponseAlternator(this);
        }


        /// <summary>
        /// 수신버퍼의 크기를 변경합니다.
        /// 새로운 버퍼의 크기는 기존 버퍼의 크기보다 커야합니다.
        /// 버퍼 크기가 변경되더라도 기존의 데이터는 유지됩니다.
        /// </summary>
        /// <param name="recvBufferSize">변경할 수신버퍼의 크기(Byte)</param>
        public override void SetReceiveBufferSize(Int32 recvBufferSize)
        {
            if (recvBufferSize <= _receivedBuffer.BufferSize)
                return;

            StreamBuffer oldBuffer = new StreamBuffer(_receivedBuffer, 0, _receivedBuffer.WrittenBytes);

            _receivedBuffer = new StreamBuffer(recvBufferSize);
            _receivedBuffer.Write(oldBuffer.Buffer, 0, oldBuffer.WrittenBytes);
        }


        public override void Close()
        {
            base.Close();
            _receivedBuffer.Clear();
            _dispatchBuffer.Clear();
        }


        internal override void WaitForReceive()
        {
            AegisTask.Run(() =>
            {
                Boolean ret = true;


                try
                {
                    lock (this)
                    {
                        if (Socket == null)
                            return;


                        if (_receivedBuffer.WritableSize == 0)
                            Logger.Write(LogType.Err, 1, "There is no remaining capacity of the receive buffer.");

                        if (Socket.Connected)
                        {
                            _saeaRecv.SetBuffer(_receivedBuffer.Buffer, _receivedBuffer.WrittenBytes, _receivedBuffer.WritableSize);
                            ret = Socket.ReceiveAsync(_saeaRecv);
                        }
                        else
                            Close();
                    }

                    if (ret == false)
                        OnComplete_Receive(null, _saeaRecv);
                }
                catch (Exception)
                {
                    Close();
                }
            });
        }


        private void OnComplete_Receive(object sender, SocketAsyncEventArgs saea)
        {
            try
            {
                lock (this)
                {
                    //  transBytes가 0이면 원격지 혹은 네트워크에 의해 연결이 끊긴 상태
                    Int32 transBytes = saea.BytesTransferred;
                    if (transBytes == 0)
                    {
                        Close();
                        return;
                    }


                    _receivedBuffer.Write(transBytes);
                    while (_receivedBuffer.ReadableSize > 0)
                    {
                        _dispatchBuffer.Clear();
                        _dispatchBuffer.Write(_receivedBuffer.Buffer, _receivedBuffer.ReadBytes, _receivedBuffer.ReadableSize);


                        //  패킷 하나가 정상적으로 수신되었는지 확인
                        Int32 packetSize;
                        if (PacketValidator == null ||
                            PacketValidator(this, _dispatchBuffer, out packetSize) == false)
                            break;

                        try
                        {
                            //  수신처리(Dispatch)
                            _receivedBuffer.Read(packetSize);
                            _dispatchBuffer.ResetReadIndex();

                            if (_alternator.Dispatch(_dispatchBuffer) == false &&
                                NetworkEvent_Received != null)
                            {
                                NetworkEvent_Received(this, _dispatchBuffer);
                            }
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
                Close();
            }
            catch (Exception e)
            {
                Logger.Write(LogType.Err, 1, e.ToString());
            }
        }


        /// <summary>
        /// 패킷을 전송합니다.
        /// </summary>
        /// <param name="buffer">보낼 데이터가 담긴 버퍼</param>
        /// <param name="offset">source에서 전송할 시작 위치</param>
        /// <param name="size">source에서 전송할 크기(Byte)</param>
        public override void SendPacket(byte[] buffer, Int32 offset, Int32 size)
        {
            try
            {
                SocketAsyncEventArgs saea;


                lock (_queueSaeaSend)
                {
                    if (_queueSaeaSend.Count() == 0)
                    {
                        saea = new SocketAsyncEventArgs();
                        saea.Completed += OnComplete_Send;
                    }
                    else
                        saea = _queueSaeaSend.Dequeue();
                }

                lock (this)
                {
                    if (Socket == null)
                    {
                        lock (_queueSaeaSend)
                        {
                            _queueSaeaSend.Enqueue(saea);
                        }
                        return;
                    }

                    saea.SetBuffer(buffer, offset, size);
                    if (Socket.SendAsync(saea) == false && NetworkEvent_Sent != null)
                        NetworkEvent_Sent(this, saea.BytesTransferred);
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


        /// <summary>
        /// 패킷을 전송합니다.
        /// </summary>
        /// <param name="buffer">전송할 데이터가 담긴 StreamBuffer</param>
        public override void SendPacket(StreamBuffer buffer)
        {
            try
            {
                SocketAsyncEventArgs saea;


                lock (_queueSaeaSend)
                {
                    if (Socket == null)
                        return;

                    if (_queueSaeaSend.Count() == 0)
                    {
                        saea = new SocketAsyncEventArgs();
                        saea.Completed += OnComplete_Send;
                    }
                    else
                        saea = _queueSaeaSend.Dequeue();
                }

                lock (this)
                {
                    if (Socket == null)
                    {
                        lock (_queueSaeaSend)
                        {
                            _queueSaeaSend.Enqueue(saea);
                        }
                        return;
                    }

                    saea.SetBuffer(buffer.Buffer, 0, buffer.WrittenBytes);
                    if (Socket.SendAsync(saea) == false && NetworkEvent_Sent != null)
                        NetworkEvent_Sent(this, saea.BytesTransferred);
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


        /// <summary>
        /// 패킷을 전송하고, 특정 패킷이 수신될 경우 dispatcher에 지정된 핸들러를 실행합니다.
        /// 이 기능은 AwaitableMethod보다는 빠르지만, 동시에 많이 호출될 경우 성능이 저하될 수 있습니다.
        /// </summary>
        /// <param name="buffer">전송할 데이터가 담긴 StreamBuffer</param>
        /// <param name="determinator">dispatcher에 지정된 핸들러를 호출할 것인지 여부를 판단하는 함수를 지정합니다.</param>
        /// <param name="dispatcher">실행될 함수를 지정합니다.</param>
        public void SendPacket(StreamBuffer buffer, PacketDeterminator determinator, EventHandler_Receive dispatcher)
        {
            if (determinator == null || dispatcher == null)
                throw new AegisException(ResultCode.InvalidArgument, "The argument determinator and dispatcher cannot be null.");


            try
            {
                SocketAsyncEventArgs saea;


                lock (_queueSaeaSend)
                {
                    if (Socket == null)
                        return;

                    if (_queueSaeaSend.Count() == 0)
                    {
                        saea = new SocketAsyncEventArgs();
                        saea.Completed += OnComplete_Send;
                    }
                    else
                        saea = _queueSaeaSend.Dequeue();
                }


                lock (this)
                {
                    if (Socket == null)
                    {
                        lock (_queueSaeaSend)
                        {
                            _queueSaeaSend.Enqueue(saea);
                        }
                        return;
                    }

                    _alternator.Add(determinator, dispatcher);
                    saea.SetBuffer(buffer.Buffer, 0, buffer.WrittenBytes);
                    if (Socket.SendAsync(saea) == false && NetworkEvent_Sent != null)
                        NetworkEvent_Sent(this, saea.BytesTransferred);
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


        private void OnComplete_Send(object sender, SocketAsyncEventArgs saea)
        {
            try
            {
                if (NetworkEvent_Sent != null)
                {
                    Int32 transBytes = saea.BytesTransferred;
                    NetworkEvent_Sent(this, transBytes);
                }
            }
            catch (SocketException)
            {
            }
            catch (Exception e)
            {
                Logger.Write(LogType.Err, 1, e.ToString());
            }


            AegisTask.Run(() =>
            {
                lock (_queueSaeaSend)
                {
                    _queueSaeaSend.Enqueue(saea);
                }
            });
        }
    }
}
