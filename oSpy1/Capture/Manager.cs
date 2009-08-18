//
// Copyright (c) 2007-2008 Ole André Vadla Ravnås <oleavr@gmail.com>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Ipc;
using System.Runtime.Serialization.Formatters;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using EasyHook;

namespace oSpy.Capture
{
    public interface IManager
    {
        void Submit(MessageQueueElement[] elements);
        void Ping();
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
    public class MessageQueueElement
    {
        /* Common fields */
        public WinApi.SYSTEMTIME time;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string process_name;
        public UInt32 process_id;
        public UInt32 thread_id;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string function_name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Manager.BACKTRACE_BUFSIZE)]
        public string backtrace;

        public UInt32 resource_id;

        public MessageType msg_type;

        /* MessageType.Message */
        public MessageContext context;
        public UInt32 domain;
        public UInt32 severity;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string message;

        /* MessageType.Packet */
        public PacketDirection direction;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string local_address;
        public UInt32 local_port;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string peer_address;
        public UInt32 peer_port;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = Manager.PACKET_BUFSIZE)]
        public byte[] buf;
        public UInt32 len;
    };

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
    public class SoftwallRule
    {
        /* mask of conditions */
        public Int32 conditions;

        /* condition values */
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string process_name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string function_name;
        public UInt32 return_address;
        public UInt32 local_address;
        public UInt32 local_port;
        public UInt32 remote_address;
        public UInt32 remote_port;

        /* return value and lasterror to set if all conditions match */
        public Int32 retval;
        public UInt32 last_error;
    };

    internal class Manager : MarshalByRefObject, IManager
    {
        public delegate void ElementsReceivedHandler(MessageQueueElement[] elements);
        public event ElementsReceivedHandler MessageElementsReceived;

        public const int PACKET_BUFSIZE = 65536;
        public const int BACKTRACE_BUFSIZE = 384;

        public const int SOFTWALL_CONDITION_PROCESS_NAME = 1;
        public const int SOFTWALL_CONDITION_FUNCTION_NAME = 2;
        public const int SOFTWALL_CONDITION_RETURN_ADDRESS = 4;
        public const int SOFTWALL_CONDITION_LOCAL_ADDRESS = 8;
        public const int SOFTWALL_CONDITION_LOCAL_PORT = 16;
        public const int SOFTWALL_CONDITION_REMOTE_ADDRESS = 32;
        public const int SOFTWALL_CONDITION_REMOTE_PORT = 64;

        /* connect() errors */
        public const int WSAEHOSTUNREACH = 10065;

        private Process[] m_processes = null;
        private SoftwallRule[] m_softwallRules = null;

        private IProgressFeedback progress = null;

        private Thread startWorkerThread;
        private IpcServerChannel m_channel;
        private string m_channelName;

        public Manager()
        {
        }

        public override object InitializeLifetimeService()
        {
            return null; // live forever
        }

        public void Submit(MessageQueueElement[] elements)
        {
            lock (MessageElementsReceived)
            {
                MessageElementsReceived(elements);
            }
        }

        public void Ping()
        {
        }

        public void StartCapture(Process[] processes, SoftwallRule[] softwallRules, IProgressFeedback progress)
        {
            this.m_processes = processes;
            this.m_softwallRules = softwallRules;
            this.progress = progress;

            startWorkerThread = new Thread(DoStartCapture);
            startWorkerThread.Start();
        }

        public void StopCapture(IProgressFeedback progress)
        {
            this.progress = progress;

            startWorkerThread = null;
            RemotingServices.Disconnect(this);
            ChannelServices.UnregisterChannel(m_channel);
            m_channel = null;
            m_channelName = null;

            progress.OperationComplete();
        }

        private void DoStartCapture()
        {
            try
            {
                PrepareCapture();
                DoInjection();
                progress.OperationComplete();
            }
            catch (Exception e)
            {
                progress.OperationFailed(e.Message);
                return;
            }
        }

        private void PrepareCapture()
        {
            m_channelName = GenerateChannelName();
            m_channel = CreateServerChannel(m_channelName);
            ChannelServices.RegisterChannel(m_channel, false);
            RemotingServices.Marshal(this, m_channelName, typeof(IManager));
        }

        private void DoInjection()
        {
            for (int i = 0; i < m_processes.Length; i++)
            {
                int percentComplete = (int)(((float)(i + 1) / (float)m_processes.Length) * 100.0f);
                progress.ProgressUpdate("Injecting logging agents", percentComplete);
                RemoteHooking.Inject(m_processes[i].Id, "oSpyAgent.dll", "oSpyAgent.dll", m_channelName, m_softwallRules);
            }
        }

        // These two are based on similar utility methods in EasyHook:
        private static IpcServerChannel CreateServerChannel(string channelName)
        {
            Dictionary<string, string> properties = new Dictionary<string, string>();
            properties["name"] = channelName;
            properties["portName"] = channelName;

            DiscretionaryAcl dacl = new DiscretionaryAcl(false, false, 1);
            dacl.AddAccess(
                AccessControlType.Allow,
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                -1,
                InheritanceFlags.None,
                PropagationFlags.None);

            CommonSecurityDescriptor secDesc = new CommonSecurityDescriptor(
                false,
                false,
                ControlFlags.GroupDefaulted | ControlFlags.OwnerDefaulted | ControlFlags.DiscretionaryAclPresent,
                null,
                null,
                null,
                dacl);

            BinaryServerFormatterSinkProvider sinkProv = new BinaryServerFormatterSinkProvider();
            sinkProv.TypeFilterLevel = TypeFilterLevel.Full;

            return new IpcServerChannel(properties, sinkProv, secDesc);
        }

        private static string GenerateChannelName()
        {
            byte[] data = new byte[30];
            RNGCryptoServiceProvider rnd = new RNGCryptoServiceProvider();
            rnd.GetBytes(data);

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < 20 + data[0] % 10; i++)
            {
                byte b = (byte) (data[i] % 62);

                if (b >= 0 && b <= 9)
                    builder.Append((char) ('0' + b));
                else if (b >= 10 && b <= 35)
                    builder.Append((char) ('A' + (b - 10)));
                else if (b >= 36 && b <= 61)
                    builder.Append((char) ('a' + (b - 36)));
            }

            return builder.ToString();
        }
    }
}