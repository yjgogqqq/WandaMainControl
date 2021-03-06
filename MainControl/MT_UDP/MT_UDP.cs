﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MainControl.MT_UDP
{
    #region //DOF_state枚举
    public enum DOF_state:byte
    {
        dof_stop = 0,           //
        dof_sys_moving = 1,     //
        dof_neutral = 2,        //
        dof_working = 3,        //
        dof_setconf = 3,        //
        dof_select = 8,
        dof_check_id = 55,
        dof_closed = 253,
        dof_emg = 254,
        dof_err = 255
    };
    #endregion
    #region //M_nCmd枚举
    public enum M_nCmd:byte
    {
        S_CMD_RUN = 0,                                  //正常运行
        S_CMD_Check = 1,
        S_CMD_Back2MID = 2,                             // 底回中立位
        S_CMD_ToMerg = 3,                               //	紧急停机
        S_CMD_ToWork = 4,                               // 握手协议
        S_CMD_JOG = 5,                                  //单缸手动	
        S_CMD_Work = 6,                                 //由低位上升到中位
        S_CMD_Stop = 7,                                 //由中位回落到低位
        S_CMD_ChaConf = 8,                              //配置驱动器信息
        S_CMD_HOM = 9,
        S_CMD_JOYCTRL = 101,
        S_CMD_GAMECTRL = 102,
    };
    #endregion
    #region //协议结构体定义
    [StructLayoutAttribute(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct DataToDOF
    {
        public byte nCheckID;
        public byte nCmd;
        public byte nAct;
        public byte nReserved;                                 //保留
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public float[] DOFs;                                  //{横摇，纵倾，航向，前向，侧向，升降	}
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] Vxyz;                                  //[0] 代表发动机转速  [1] 代表时速。
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] Axyz;      //...
    };


    public struct DataToHost
    {
        public byte nCheckID;
        public byte nDOFStatus;
        public byte nRev0;                                     //需要使用
        public byte nRev1;                                     //需要使用
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public float[] attitude;                              //需要使用，RotorTrim，MixtureControl
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public float[] para;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public float[] motor_code;
    };
    #endregion
    public class MtUdp 
    {
        delegate double UdpReceiveHandler(IPEndPoint udpEndPoint);
        #region /*变量定义*/
        private const int deviceAmount = 4;

        public static int DeviceAmount
        {
            get { return MtUdp.deviceAmount; }
        }

        public bool[] m_DeviceConnectState = new bool[deviceAmount] { false, false, false, false };
        public int[] m_DeviceConnectCheckDelay = new int[deviceAmount] { 0, 0, 0, 0 };
        public DataToDOF m_sToDOFBuf = new DataToDOF();
        public DataToHost[] m_sToHostBuf = new DataToHost[deviceAmount];                            //根据外部设备数量定义相应数据结构体
        public IPEndPoint m_CurRomateIpEndPoint = new IPEndPoint(0, 0);
        public IPEndPoint[] m_RemoteIpEndpoint = new IPEndPoint[deviceAmount];                      //根据外部设备数量定义地址数量
        public IPEndPoint m_PlcIpEndpoint = new IPEndPoint(IPAddress.Parse("192.168.0.120"), 5000);
        IPEndPoint m_LocalIPEndPoint = new IPEndPoint(IPAddress.Parse("192.168.0.130"), 10000);
        IPEndPoint m_AnyRemoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
        public byte[] m_UdpReceiveBuf;
        UdpClient m_listener;
        public Thread m_UdpDataProcess;

        public byte[] m_DataFromPlc = new byte[8];
        public byte[] m_DataToPlc = new byte[4];
        #endregion
        #region //Udp初始化
        public void UdpInit(int udpPort)
        {
            try
            {
                #region /*变量初始化*/
                for (int i = 0; i < DeviceAmount; i++)
                {
                    m_RemoteIpEndpoint[i] = new IPEndPoint(IPAddress.Parse("192.168.0.13" + (i + 1)), 10000);
                }
                m_sToDOFBuf.DOFs = new float[6];
                m_sToDOFBuf.Vxyz = new float[3];
                m_sToDOFBuf.Axyz = new float[3];
                for (int i=0;i< DeviceAmount; i++)
                {
                    m_sToHostBuf[i].attitude = new float[6];
                    m_sToHostBuf[i].para = new float[6];
                    m_sToHostBuf[i].motor_code = new float[6];
                }
                #endregion
                m_listener = new UdpClient(udpPort);
                m_listener.Client.Blocking = true;
                m_UdpDataProcess = new Thread(UdpDataProcess);
                m_UdpDataProcess.IsBackground = true;
                m_UdpDataProcess.Start();
            }
            catch (Exception e)
            {
                if (MessageBox.Show(e.Message, "初始化套接字错误！", MessageBoxButton.OK, MessageBoxImage.Error) == MessageBoxResult.OK)
                {
                    Environment.Exit(0);
                }
            }
        }
        #endregion
        
        #region //UDP接收数据处理
        public void UdpDataProcess()
        {
            while (true)
            {
                m_UdpReceiveBuf = m_listener.Receive(ref m_AnyRemoteIpEndPoint);
                for (int i = 0; i < 4; i++)
                {
                    if (m_CurRomateIpEndPoint.Equals(m_RemoteIpEndpoint[i]))
                    {
                        m_sToHostBuf[i] = (DataToHost)ByteToStruct(m_UdpReceiveBuf, typeof(DataToHost));
                        m_DeviceConnectCheckDelay[i] = 0;
                        m_DeviceConnectState[i] = true;
                    }
                    else
                    {
                        m_DeviceConnectCheckDelay[i]++;
                        if (m_DeviceConnectCheckDelay[i] > 100)
                        {
                            m_DeviceConnectState[i] = false;
                        }
                    }
                }
                if (m_CurRomateIpEndPoint.Equals(m_PlcIpEndpoint))
                {
                    Array.Copy(m_UdpReceiveBuf, m_DataFromPlc, m_DataFromPlc.Length);
                }
            }
        } 
        #endregion

        #region //寻底，上升至中位
        public void DofUpToMedian(IPEndPoint endPoint)
        {
            m_sToDOFBuf.nCheckID = 55;
            m_sToDOFBuf.nCmd = (byte)M_nCmd.S_CMD_Work;
            m_sToDOFBuf.nAct = 0;
            m_sToDOFBuf.nReserved = 0;
            for (int i = 0; i < 3; i++)
            {
                m_sToDOFBuf.DOFs[i] = 0.0f;
                m_sToDOFBuf.DOFs[i+3] = 0.0f;
                m_sToDOFBuf.Vxyz[i] = 0.0f;
                m_sToDOFBuf.Axyz[i] = 0.0f;
            }
            m_listener.Send(StructToBytes(m_sToDOFBuf,Marshal.SizeOf(m_sToDOFBuf)), Marshal.SizeOf(m_sToDOFBuf), endPoint);
        }
        #endregion
        #region //运行
        public void DofToRun(IPEndPoint endPoint)
        {
            m_sToDOFBuf.nCheckID = 55;
            m_sToDOFBuf.nCmd = (byte)M_nCmd.S_CMD_RUN;
            m_sToDOFBuf.nAct = 0;
            m_sToDOFBuf.nReserved = 0;
            for (int i = 0; i < 3; i++)
            {
                m_sToDOFBuf.DOFs[i] = 0.0f;
                m_sToDOFBuf.DOFs[i + 3] = 0.0f;
                m_sToDOFBuf.Vxyz[i] = 0.0f;
                m_sToDOFBuf.Axyz[i] = 0.0f;
            }
            m_listener.Send(StructToBytes(m_sToDOFBuf, Marshal.SizeOf(m_sToDOFBuf)), Marshal.SizeOf(m_sToDOFBuf), endPoint);
        } 
        #endregion
        #region //任意姿态到中位

        public void DofToMedain(IPEndPoint endPoint)
        {
            m_sToDOFBuf.nCheckID = 55;
            m_sToDOFBuf.nCmd = (byte)M_nCmd.S_CMD_Back2MID;
            m_sToDOFBuf.nAct = 0;
            m_sToDOFBuf.nReserved = 0;
            for (int i = 0; i < 3; i++)
            {
                m_sToDOFBuf.DOFs[i] = 0.0f;
                m_sToDOFBuf.DOFs[i + 3] = 0.0f;
                m_sToDOFBuf.Vxyz[i] = 0.0f;
                m_sToDOFBuf.Axyz[i] = 0.0f;
            }
            m_listener.Send(StructToBytes(m_sToDOFBuf, Marshal.SizeOf(m_sToDOFBuf)), Marshal.SizeOf(m_sToDOFBuf), endPoint);
        } 
        #endregion

        #region //到底位
        public void DofToBottom(IPEndPoint endPoint)
        {
            m_sToDOFBuf.nCheckID = 55;
            m_sToDOFBuf.nCmd = (byte)M_nCmd.S_CMD_Stop;
            m_sToDOFBuf.nAct = 0;
            m_sToDOFBuf.nReserved = 0;
            for (int i = 0; i < 3; i++)
            {
                m_sToDOFBuf.DOFs[i] = 0.0f;
                m_sToDOFBuf.DOFs[i + 3] = 0.0f;
                m_sToDOFBuf.Vxyz[i] = 0.0f;
                m_sToDOFBuf.Axyz[i] = 0.0f;
            }
            m_listener.Send(StructToBytes(m_sToDOFBuf, Marshal.SizeOf(m_sToDOFBuf)), Marshal.SizeOf(m_sToDOFBuf), endPoint);
        } 
        #endregion

        #region //急停
        public void DofToEmergency(IPEndPoint endPoint)
        {
            m_sToDOFBuf.nCheckID = 55;
            m_sToDOFBuf.nCmd = (byte)M_nCmd.S_CMD_ToMerg;
            m_sToDOFBuf.nAct = 0;
            m_sToDOFBuf.nReserved = 0;
            for (int i = 0; i < 3; i++)
            {
                m_sToDOFBuf.DOFs[i] = 0.0f;
                m_sToDOFBuf.DOFs[i + 3] = 0.0f;
                m_sToDOFBuf.Vxyz[i] = 0.0f;
                m_sToDOFBuf.Axyz[i] = 0.0f;
            }
            m_listener.Send(StructToBytes(m_sToDOFBuf, Marshal.SizeOf(m_sToDOFBuf)), Marshal.SizeOf(m_sToDOFBuf), endPoint);
        }
        #endregion

        #region //发送数据到PLC
        public void SendDataToPlc(byte[] dgram, int bytes, IPEndPoint endPoint)
        {
            m_listener.Send(dgram, bytes, endPoint);
        } 
        #endregion
        #region //结构体转字节数组
        public byte[] StructToBytes(object structObj, int size)
        {
            byte[] bytes = new byte[size];
            IntPtr structPtr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(structObj, structPtr, false);
            Marshal.Copy(structPtr, bytes, 0, size);
            Marshal.FreeHGlobal(structPtr);
            return bytes;
        } 
        #endregion

        #region //字节数组转结构体
        public object ByteToStruct(byte[] bytes, Type type)
        {
            int size = Marshal.SizeOf(type);
            if (size > bytes.Length)
            {
                return null;
            }
            IntPtr structPtr = Marshal.AllocHGlobal(size);
            Marshal.Copy(bytes, 0, structPtr, size);
            object obj = Marshal.PtrToStructure(structPtr, type);
            Marshal.FreeHGlobal(structPtr);
            return obj;
        } 
        #endregion
    }
}
