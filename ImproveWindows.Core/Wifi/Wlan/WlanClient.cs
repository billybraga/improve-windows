using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ImproveWindows.Core.Wifi.Wlan
{
    public sealed class WlanClient : IDisposable
    {
        // FIELDS =================================================================

        internal IntPtr ClientHandle;

        private readonly Dictionary<Guid, WlanInterface> _interfaceMap = new();
        private volatile WlanInterface[] _interfaceList;

        private WlanHostedNetwork? _hostedNetwork;
        private readonly object _hostedNetworkLock = new();
        private readonly NativeMethods.WlanNotificationCallbackDelegate _notificationCallbackDelegate;

        // PROPERTIES =============================================================

        public Version ClientVersion
        {
            get
            {
                OperatingSystem os = Environment.OSVersion;
                if (os.Platform == PlatformID.Win32NT)
                {
                    Version vs = os.Version;
                    if (vs.Major >= 6)
                    {
                        return new Version(2, 0);
                    }

                    if (vs is { Major: 5, Minor: >= 1 })
                    {
                        return new Version(1, 0);
                    }
                }

                return new Version(0, 0);
            }
        }

        public Version NegotiatedVersion { get; private set; }

        public AutoConfigClass AutoConfig { get; private set; }

        public WlanHostedNetwork? HostedNetwork
        {
            get
            {
                if (_hostedNetwork == null)
                {
                    lock (_hostedNetworkLock)
                    {
                        if (_hostedNetwork == null)
                        {
                            try
                            {
                                _hostedNetwork = WlanHostedNetwork.CreateHostedNetwork(this);
                            }
                            catch (EntryPointNotFoundException)
                            {
                                System.Console.Out.WriteLine("System is not Hosted Network capable."); //TODO Address using logger
                            }
                        }
                    }
                }

                return _hostedNetwork;
            }
        }

        public WlanInterface[] Interfaces => _interfaceList;

        // CONSTRUCTORS, DESTRUCTOR ===============================================

        private void ReloadInterfaces()
        {
            Util.ThrowIfError(
                NativeMethods.WlanEnumInterfaces(ClientHandle, IntPtr.Zero, out var listPtr)
            );
            try
            {
                WlanInterfaceInfoList list = (WlanInterfaceInfoList)(
                    Marshal.PtrToStructure(listPtr, typeof(WlanInterfaceInfoList))
                    ?? throw new InvalidOperationException("PtrToStructure returned null")
                );
                uint numberOfItems = list.NumberOfItems;
                Int64 listIterator = listPtr.ToInt64() + Marshal.OffsetOf(typeof(WlanInterfaceInfoList), "InterfaceInfo").ToInt64();
                WlanInterface[] interfaces = new WlanInterface[numberOfItems];
                List<Guid> currentIfaceGuids = new List<Guid>();
                for (int i = 0; i < numberOfItems; i++)
                {
                    WlanInterfaceInfo info =
                        (WlanInterfaceInfo)(Marshal.PtrToStructure(new IntPtr(listIterator), typeof(WlanInterfaceInfo)) ?? throw new InvalidOperationException("PtrToStructure returned null"));
                    listIterator += Marshal.SizeOf(info);
                    currentIfaceGuids.Add(info.Guid);
                    if (!_interfaceMap.TryGetValue(info.Guid, out var wlanInterface))
                    {
                        wlanInterface = WlanInterface.CreateInterface(this, info);
                    }

                    interfaces[i] = wlanInterface;
                    _interfaceMap[info.Guid] = wlanInterface;
                }

                // Remove stale interfaceList
                Queue<Guid> deadIfacesGuids = new Queue<Guid>();
                foreach (Guid ifaceGuid in _interfaceMap.Keys)
                {
                    if (!currentIfaceGuids.Contains(ifaceGuid))
                        deadIfacesGuids.Enqueue(ifaceGuid);
                }

                while (deadIfacesGuids.Count != 0)
                {
                    Guid deadIfaceGuid = deadIfacesGuids.Dequeue();
                    _interfaceMap.Remove(deadIfaceGuid);
                }

                _interfaceList = interfaces;
            }
            finally
            {
                NativeMethods.WlanFreeMemory(listPtr);
            }
        }

        private WlanClient()
        {
            uint clientVersionDword = Util.VersionToDword(ClientVersion);
            Util.ThrowIfError(NativeMethods.WlanOpenHandle(clientVersionDword, IntPtr.Zero, out var negotiatedVersionDword, out ClientHandle));
            NegotiatedVersion = Util.DwordToVersion(negotiatedVersionDword);
            try
            {
                _notificationCallbackDelegate = new NativeMethods.WlanNotificationCallbackDelegate(OnWlanNotification);
                WlanNotificationSource previousNotificationSource;
                Util.ThrowIfError(
                    NativeMethods.WlanRegisterNotification(
                        ClientHandle,
                        WlanNotificationSource.All,
                        false,
                        _notificationCallbackDelegate,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        out previousNotificationSource
                    )
                );
            }
            catch (Win32Exception)
            {
                NativeMethods.WlanCloseHandle(ClientHandle, IntPtr.Zero);
                throw;
            }
        }

        ~WlanClient()
        {
            if (ClientHandle != IntPtr.Zero)
            {
                NativeMethods.WlanCloseHandle(ClientHandle, IntPtr.Zero);
                ClientHandle = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (ClientHandle != IntPtr.Zero)
            {
                NativeMethods.WlanCloseHandle(ClientHandle, IntPtr.Zero);
                ClientHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Creates a Native Wi-Fi Client control class.
        /// </summary>
        /// <returns>Wlan Client instance.</returns>
        /// <exception cref="Win32Exception">On any error related to opening handle, registering notifications.</exception>
        /// <exception cref="EntryPointNotFoundException">When WlanApi is not available.</exception>
        public static WlanClient CreateClient()
        {
            WlanClient client = new WlanClient();
            client.AutoConfig = new AutoConfigClass(client);

            client.ReloadInterfaces();
            client.AcmInterfaceArrived += (sender, e) => { client.ReloadInterfaces(); };
            client.AcmInterfaceRemoved += (sender, e) => { client.ReloadInterfaces(); };

            if (client.HostedNetwork != null)
            {
                Util.ThrowIfError(NativeMethods.WlanRegisterVirtualStationNotification(client.ClientHandle, true, IntPtr.Zero));
            }

            return client;
        }

        // EVENTS =============================================================

        private WlanConnectionNotificationData? ParseWlanConnectionNotificationData(ref WlanNotificationData notifyData)
        {
            int expectedSize = Marshal.SizeOf(typeof(WlanConnectionNotificationData));
            if (notifyData.DataSize < expectedSize)
                return null;

            WlanConnectionNotificationData connNotifyData =
                (WlanConnectionNotificationData)
                Marshal.PtrToStructure(notifyData.Data, typeof(WlanConnectionNotificationData));
            if (connNotifyData.ReasonCode == WlanReasonCode.Success)
            {
                IntPtr profileXmlPtr = new IntPtr(
                    notifyData.Data.ToInt64() + Marshal.OffsetOf(typeof(WlanConnectionNotificationData), "ProfileXml").ToInt64()
                );
                connNotifyData.ProfileXml = Marshal.PtrToStringUni(profileXmlPtr);
            }

            return connNotifyData;
        }

        internal void OnWlanNotification(ref WlanNotificationData data, IntPtr context)
        {
            WlanInterface wlanIface = null;
            WlanEventArgs wlanEvent = WlanEventArgs.Empty;
            if (_interfaceMap.ContainsKey(data.InterfaceGuid))
            {
                wlanIface = _interfaceMap[data.InterfaceGuid];
                wlanEvent.InterfaceGuid = wlanIface.Guid;
            }

            wlanEvent.NotificationSource = data.NotificationSource;
            wlanEvent.NotificationCode = data.NotificationCode;

            switch (data.NotificationSource)
            {
                case WlanNotificationSource.Acm:
                    switch ((WlanAcmNotificationCode)data.NotificationCode)
                    {
                        case WlanAcmNotificationCode.ConnectionStart:
                        case WlanAcmNotificationCode.ConnectionAttemptFail:
                        case WlanAcmNotificationCode.Disconnecting:
                        case WlanAcmNotificationCode.Disconnected:
                            break;
                        case WlanAcmNotificationCode.ConnectionComplete:
                            WlanConnectionNotificationData? conNotif = ParseWlanConnectionNotificationData(ref data);
                            if (conNotif.HasValue && wlanIface != null)
                            {
                                AcmConnectionEventArgs conArgs = new AcmConnectionEventArgs();
                                conArgs.ConnectionData = conNotif.Value;
                                conArgs.NotificationCode = WlanAcmNotificationCode.ConnectionComplete;
                                wlanIface.OnAcmConnectionCompleted(conArgs);
                            }

                            break;
                        case WlanAcmNotificationCode.ScanFail:
                            //TODO parse WLAN_REASON_CODE in field Data
                            break;
                        case WlanAcmNotificationCode.BssTypeChange:
                            //TODO parse DOT11_BSS_TYPE in field Data
                            break;
                        case WlanAcmNotificationCode.PowerSettingChange:
                            //TODO parse WLAN_POWER_SETTING in field Data
                            break;
                        case WlanAcmNotificationCode.ProfileNameChange:
                            //TODO parse two null-terminated WCHAR strings in field Data
                            break;
                        case WlanAcmNotificationCode.AdhocNetworkStateChange:
                            //TODO parse WLAN_ADHOC_NETWORK_STATE in field Data
                            break;
                        case WlanAcmNotificationCode.ScreenPowerChange:
                            //TODO parse BOOL in field Data
                            break;
                    }

                    OnAcmNotification(new AcmEventArgs());
                    break;
                case WlanNotificationSource.Msm:
                    switch ((WlanMsmNotificationCode)data.NotificationCode)
                    {
                        case WlanMsmNotificationCode.Associating:
                        case WlanMsmNotificationCode.Associated:
                        case WlanMsmNotificationCode.Authenticating:
                        case WlanMsmNotificationCode.Connected:
                        case WlanMsmNotificationCode.RoamingStart:
                        case WlanMsmNotificationCode.RoamingEnd:
                        case WlanMsmNotificationCode.Disassociating:
                        case WlanMsmNotificationCode.Disconnected:
                        case WlanMsmNotificationCode.PeerJoin:
                        case WlanMsmNotificationCode.PeerLeave:
                        case WlanMsmNotificationCode.AdapterRemoval:
                            //TODO parse WLAN_MSM_NOTIFICATION_DATA in field Data
                            break;
                        case WlanMsmNotificationCode.AdapterOperationModeChange:
                            //TODO parse ULONG in field Data
                            break;
                        case WlanMsmNotificationCode.SignalQualityChange:
                            //TODO parse ULONG for WLAN_SIGNAL_QUALITY in field Data
                            break;
                        case WlanMsmNotificationCode.RadioStateChange:
                        {
                            WlanPhyRadioState radioState = Util.ParseStruct<WlanPhyRadioState>(data.Data, data.DataSize);
                            MsmRadioStateChangeEventArgs eventArgs = new MsmRadioStateChangeEventArgs();
                            eventArgs.RadioState = radioState;
                            wlanIface.OnMsmRadioStateChange(eventArgs);
                        }
                            break;
                    }

                    OnMsmNotification(new MsmEventArgs());
                    break;
                case WlanNotificationSource.OneX:
                    OnOneXNotification(new OneXEventArgs());
                    break; //TODO
                case WlanNotificationSource.Security:
                    OnSecurityNotification(new SecurityEventArgs());
                    break; //TODO
                case WlanNotificationSource.Ihv:
                {
                    IhvEventArgs eventArgs = new IhvEventArgs();
                    eventArgs.Data = data.Data;
                    eventArgs.DataSize = data.DataSize;
                    OnIhvNotification(eventArgs);
                }
                    break; //TODO
                case WlanNotificationSource.HostedNetwork:
                    if (HostedNetwork != null)
                    {
                        switch ((WlanHostedNetworkNotificationCode)data.NotificationCode)
                        {
                            case WlanHostedNetworkNotificationCode.PeerStateChange:
                            {
                                WlanHostedNetworkDataPeerStateChange peerStateChange = Util.ParseStruct<WlanHostedNetworkDataPeerStateChange>(data.Data, data.DataSize);
                                HnwkPeerStateChangeEventArgs eventArgs = new HnwkPeerStateChangeEventArgs();
                                eventArgs.PeerState = peerStateChange;
                                HostedNetwork.OnHnwkPeerStateChange(eventArgs);
                            }
                                break;
                            case WlanHostedNetworkNotificationCode.RadioStateChange:
                            {
                                WlanHostedNetworkRadioState radioState = Util.ParseStruct<WlanHostedNetworkRadioState>(data.Data, data.DataSize);
                                HnwkRadioStateChangeEventArgs eventArgs = new HnwkRadioStateChangeEventArgs();
                                eventArgs.RadioState = radioState;
                                HostedNetwork.OnHnwkRadioStateChange(eventArgs);
                            }
                                break;
                            case WlanHostedNetworkNotificationCode.StateChange:
                            {
                                WlanHostedNetworkStateChange stateChange = Util.ParseStruct<WlanHostedNetworkStateChange>(data.Data, data.DataSize);
                                HnwkStateChangeEventArgs eventArgs = new HnwkStateChangeEventArgs();
                                eventArgs.State = stateChange;
                                HostedNetwork.OnHnwkStateChange(eventArgs);
                            }
                                break;
                        }

                        wlanEvent.InterfaceGuid = HostedNetwork.Guid;
                        OnHwnkNotification(new HnwkEventArgs());
                    }

                    break;
            }

            OnWlanNotification(wlanEvent);
        }
        // INTERNALS ==========================================================

        public class AutoConfigClass
        {
            private WlanClient client;

            internal AutoConfigClass(WlanClient client)
            {
                this.client = client;
            }

            public bool ShowDeniedNetworks
            {
                get => (bool)QueryAutoConfigParameter(WlanAutoConfOpcode.ShowDeniedNetworks);
                set => SetAutoConfigParameter(WlanAutoConfOpcode.ShowDeniedNetworks, value);
            }

            public WlanPowerSetting PowerSetting => (WlanPowerSetting)QueryAutoConfigParameter(WlanAutoConfOpcode.PowerSetting);

            public bool OnlyUseGroupProfilesForAllowedNetworks => (bool)QueryAutoConfigParameter(WlanAutoConfOpcode.OnlyUseGroupProfilesForAllowedNetworks);

            public bool AllowExplicitCredentials
            {
                get => (bool)QueryAutoConfigParameter(WlanAutoConfOpcode.AllowExplicitCredentials);
                set => SetAutoConfigParameter(WlanAutoConfOpcode.AllowExplicitCredentials, value);
            }

            public uint BlockPeriod
            {
                get => (uint)QueryAutoConfigParameter(WlanAutoConfOpcode.BlockPeriod);
                set => SetAutoConfigParameter(WlanAutoConfOpcode.BlockPeriod, value);
            }

            public bool AllowVirtualStationExtensibility
            {
                get => (bool)QueryAutoConfigParameter(WlanAutoConfOpcode.AllowVirtualStationExtensibility);
                set => SetAutoConfigParameter(WlanAutoConfOpcode.AllowVirtualStationExtensibility, value);
            }

            #region AutoConfigInternal

            /// <summary>
            /// The function queries for the parameters of the auto configuration service.
            /// </summary>
            /// <param name="opcode">Property to be get.</param>
            /// <returns>Value type.</returns>
            /// <exception cref="Win32Exception">When error occurs during WlanApi call.</exception>
            private ValueType QueryAutoConfigParameter(WlanAutoConfOpcode opcode)
            {
                uint dataSize;
                WlanOpcodeValueType valueType;
                Util.ThrowIfError(NativeMethods.WlanQueryAutoConfigParameter(client.ClientHandle, opcode, IntPtr.Zero, out dataSize, out var data, out valueType));
                ValueType value = null;
                switch (opcode)
                {
                    case WlanAutoConfOpcode.ShowDeniedNetworks:
                    case WlanAutoConfOpcode.OnlyUseGroupProfilesForAllowedNetworks:
                    case WlanAutoConfOpcode.AllowExplicitCredentials:
                    case WlanAutoConfOpcode.AllowVirtualStationExtensibility:
                        value = Convert.ToBoolean(Marshal.ReadInt32(data));
                        break;
                    case WlanAutoConfOpcode.BlockPeriod:
                        value = Convert.ToUInt32(Marshal.ReadInt32(data));
                        break;
                    case WlanAutoConfOpcode.PowerSetting:
                        value = (WlanPowerSetting)Marshal.PtrToStructure(data, typeof(WlanPowerSetting));
                        break;
                    default:
                        value = Marshal.ReadInt32(data);
                        break;
                }

                return value;
            }

            /// <summary>
            /// The function sets parameters for the automatic configuration service.
            /// </summary>
            /// <param name="opcode">Property to be set.</param>
            /// <param name="phy">Value to be set.</param>
            /// <exception cref="ArgumentException">If any parameter contains unacceptable phy.</exception>
            /// <exception cref="Win32Exception">When error occurs during WlanApi call.</exception>
            private void SetAutoConfigParameter(WlanAutoConfOpcode opcode, object value)
            {
                int dataSize = Marshal.SizeOf(value);
                IntPtr data = Marshal.AllocHGlobal(dataSize);
                try
                {
                    switch (opcode)
                    {
                        case WlanAutoConfOpcode.ShowDeniedNetworks:
                        case WlanAutoConfOpcode.OnlyUseGroupProfilesForAllowedNetworks:
                        case WlanAutoConfOpcode.AllowExplicitCredentials:
                        case WlanAutoConfOpcode.AllowVirtualStationExtensibility:
                            if (!(value is bool)) throw new ArgumentException();
                            Marshal.WriteInt32(data, Convert.ToInt32(value));
                            break;
                        case WlanAutoConfOpcode.BlockPeriod:
                            if (!(value is uint)) throw new ArgumentException();
                            Marshal.WriteInt32(data, Convert.ToInt32(value));
                            break;
                        default:
                            throw new ArgumentException();
                    }

                    Util.ThrowIfError(NativeMethods.WlanSetAutoConfigParameter(client.ClientHandle, opcode, (uint)dataSize, data, IntPtr.Zero));
                }
                finally
                {
                    Marshal.FreeHGlobal(data);
                }
            }

            #endregion AutoConfigInternal
        }

        // EVENTS =============================================================

        #region General Events

        public event WlanEventHandler? WlanNotification;
        public event AcmEventHandler? AcmNotification;
        public event MsmEventHandler? MsmNotification;
        public event OneXEventHandler? OneXNotification;
        public event SecurityEventHandler? SecurityNotification;
        public event IhvEventHandler? IhvNotification;
        public event HnwkEventHandler? HwnkNotification;

        internal void OnWlanNotification(WlanEventArgs eventArgs)
        {
            if (WlanNotification != null)
                WlanNotification(this, eventArgs);
        }

        internal void OnOneXNotification(OneXEventArgs e)
        {
            if (OneXNotification != null)
                OneXNotification(this, e);
        }

        internal void OnAcmNotification(AcmEventArgs e)
        {
            if (AcmNotification != null)
                AcmNotification(this, e);
        }

        internal void OnMsmNotification(MsmEventArgs e)
        {
            if (MsmNotification != null)
                MsmNotification(this, e);
        }

        internal void OnSecurityNotification(SecurityEventArgs e)
        {
            if (SecurityNotification != null)
                SecurityNotification(this, e);
        }

        internal void OnIhvNotification(IhvEventArgs e)
        {
            if (IhvNotification != null)
                IhvNotification(this, e);
        }

        internal void OnHwnkNotification(HnwkEventArgs notification)
        {
            if (HwnkNotification != null)
                HwnkNotification(this, notification);
        }

        #endregion General Events

        #region Client specific Events

        public event AcmEventHandler? AcmInterfaceArrived;
        public event AcmEventHandler? AcmInterfaceRemoved;
        public event AcmEventHandler? AcmAutoconfEnabled;
        public event AcmEventHandler? AcmAutoconfDisabled;
        public event AcmEventHandler? AcmBackgroundScanEnabled;
        public event AcmEventHandler? AcmBackgroundScanDisabled;
        public event AcmEventHandler? AcmFilterListChanged;

        internal void OnAcmInterfaceArrived(AcmEventArgs eventArgs)
        {
            if (AcmInterfaceArrived != null)
                AcmInterfaceArrived(this, eventArgs);
        }

        internal void OnAcmInterfaceRemoved(AcmEventArgs eventArgs)
        {
            if (AcmInterfaceRemoved != null)
                AcmInterfaceRemoved(this, eventArgs);
        }

        internal void OnAcmAutoconfEnabled(AcmEventArgs eventArgs)
        {
            if (AcmAutoconfEnabled != null)
                AcmAutoconfEnabled(this, eventArgs);
        }

        internal void OnAcmAutoconfDisabled(AcmEventArgs eventArgs)
        {
            if (AcmAutoconfDisabled != null)
                AcmAutoconfDisabled(this, eventArgs);
        }

        internal void OnAcmBackgroundScanEnabled(AcmEventArgs eventArgs)
        {
            if (AcmBackgroundScanEnabled != null)
                AcmBackgroundScanEnabled(this, eventArgs);
        }

        internal void OnAcmBackgroundScanDisabled(AcmEventArgs eventArgs)
        {
            if (AcmBackgroundScanDisabled != null)
                AcmBackgroundScanDisabled(this, eventArgs);
        }

        internal void OnAcmFilterListChanged(AcmEventArgs eventArgs)
        {
            if (AcmFilterListChanged != null)
                AcmFilterListChanged(this, eventArgs);
        }

        public event MsmNotificationEventHandler? MsmAdapterRemoved;

        internal void OnMsmAdapterRemoved(MsmNotificationEventArgs eventArgs)
        {
            if (MsmAdapterRemoved != null)
                MsmAdapterRemoved(this, eventArgs);
        }

        #endregion Client specific Events
    }
}