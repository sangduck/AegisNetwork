﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;



namespace Aegis.Client.Network
{
    /// <summary>
    /// Size(2 bytes), PacketId(2 bytes)로 구성된 4바이트 헤더를 갖는 기본 패킷 구성입니다.
    /// </summary>
    public class Packet : StreamBuffer
    {
        /// <summary>
        /// 현재 패킷의 크기를 가져옵니다. 패킷의 크기값은 임의로 변경할 수 없습니다.
        /// </summary>
        public UInt16 Size
        {
            get { return GetUInt16(0); }
            private set { OverwriteUInt16(0, value); }
        }
        /// <summary>
        /// 패킷의 고유번호를 지정하거나 가져옵니다.
        /// </summary>
        public UInt16 PacketId
        {
            get { return GetUInt16(2); }
            set { OverwriteUInt16(2, value); }
        }





        public Packet()
        {
            PutUInt16(0);       //  Size
            PutUInt16(0);       //  PacketId
        }


        /// <summary>
        /// 고유번호를 지정하여 패킷을 생성합니다.
        /// </summary>
        /// <param name="packetId">패킷의 고유번호</param>
        public Packet(UInt16 packetId)
        {
            PutUInt16(0);           //  Size
            PutUInt16(packetId);    //  PacketId
        }


        /// <summary>
        /// 고유번호와 패킷의 기본 크기를 지정하여 패킷을 생성합니다.
        /// </summary>
        /// <param name="packetId">패킷의 고유번호</param>
        /// <param name="capacity">패킷 버퍼의 크기</param>
        public Packet(UInt16 packetId, UInt16 capacity)
        {
            Capacity(capacity);
            PutUInt16(0);           //  Size
            PutUInt16(packetId);    //  PacketId
        }


        /// <summary>
        /// StreamBuffer의 데이터를 복사하여 패킷을 생성합니다.
        /// </summary>
        /// <param name="source">복사할 데이터가 담긴 StreamBuffer 객체</param>
        public Packet(StreamBuffer source)
        {
            Write(source.Buffer, 0, source.WrittenBytes);
        }


        /// <summary>
        /// byte 배열의 데이터를 복사하여 패킷을 생성합니다.
        /// </summary>
        /// <param name="source">복사할 데이터가 담긴 byte 배열</param>
        /// <param name="startIndex">source에서 복사할 시작 위치</param>
        /// <param name="size">복사할 크기(Byte)</param>
        public Packet(byte[] source, int startIndex, int size)
        {
            Write(source, startIndex, size);
        }


        /// <summary>
        /// 패킷 버퍼를 초기화합니다. 기존의 PacketId 값은 유지됩니다.
        /// </summary>
        public override void Clear()
        {
            UInt16 packetId = PacketId;


            base.Clear();

            PutUInt16(0);           //  Size
            PutUInt16(packetId);    //  PacketId
        }


        /// <summary>
        /// 패킷 버퍼를 초기화하고 source 데이터를 저장합니다. Packet Header의 Size는 source 버퍼의 헤더값이 사용됩니다.
        /// </summary>
        /// <param name="source">저장할 데이터</param>
        public void Clear(StreamBuffer source)
        {
            if (source.BufferSize < 4)
                throw new AegisException("The source size must be at lest 4 bytes.");

            base.Clear();
            Write(source.Buffer, 0, source.WrittenBytes);
            Size = GetUInt16(0);
        }


        /// <summary>
        /// 패킷 버퍼를 초기화하고 source 데이터를 저장합니다. Packet Header의 Size는 source 버퍼의 헤더값이 사용됩니다.
        /// </summary>
        /// <param name="source">저장할 데이터</param>
        /// <param name="index">저장할 데이터의 시작위치</param>
        /// <param name="size">저장할 데이터 크기(Byte)</param>
        public void Clear(byte[] source, int index, int size)
        {
            if (size < 4)
                throw new AegisException("The source size must be at lest 4 bytes.");

            Clear();
            Write(source, index, size);
            Size = GetUInt16(0);
        }


        /// <summary>
        /// 패킷의 크기가 변경되었을 때 호출됩니다.
        /// 이 함수가 호출되어야 패킷의 Size값이 변경됩니다.
        /// </summary>
        protected override void OnWritten()
        {
            Size = (UInt16)WrittenBytes;
        }


        /// <summary>
        /// 패킷의 헤더 위치를 건너띄어 본문 데이터를 읽을 수 있도록 읽기위치를 조절합니다.
        /// 이 함수가 호출되면 ReadIndex는 4에 위치하지만, WriteIndex는 변하지 않습니다.
        /// </summary>
        public void SkipHeader()
        {
            ResetReadIndex();

            GetUInt16();        //  Size
            GetUInt16();        //  PacketId
        }
    }
}
