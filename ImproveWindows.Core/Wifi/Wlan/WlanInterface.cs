﻿using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace ImproveWindows.Core.Wifi.Wlan
{
    public sealed class WlanInterface
    {
        // FIELDS =================================================================

        private readonly WlanClient _client;

        // PROPERTIES =============================================================

        /// <summary>
        /// A wildcard physical address specifying all physical addresses.
        /// </summary>
        public static PhysicalAddress WildcardPhysicalAddress { get; } = new(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff });

        /// <summary>
        /// Gets network interface GUID.
        /// </summary>
        public Guid Guid { get; private set; }

        //public String Description { get; private set; }

        /// <summary>
        /// Gets or sets a key indicating whether this <see cref="WlanInterface"/> is automatically configured.
        /// </summary>
        /// <key><c>true</c> if "autoconf" is enabled; otherwise, <c>false</c>.</key>
        public bool AutoConfEnabled
        {
            get => (bool)QueryInterface(WlanIntfOpcode.AutoconfEnabled);
            set => SetInterface(WlanIntfOpcode.AutoconfEnabled, value);
        }

        public bool BackgroundScanEnabled
        {
            get => (bool)QueryInterface(WlanIntfOpcode.BackgroundScanEnabled);
            set => SetInterface(WlanIntfOpcode.BackgroundScanEnabled, value);
        }

        public WlanRadioState RadioState
        {
            get => (WlanRadioState)QueryInterface(WlanIntfOpcode.RadioState);
            set => SetInterface(WlanIntfOpcode.RadioState, value);
        }

        /// <summary>
        /// Gets or sets the BSS type for the indicated interface.
        /// </summary>
        /// <key>The type of the BSS.</key>
        public Dot11BssType BssType
        {
            get => (Dot11BssType)QueryInterface(WlanIntfOpcode.BssType);
            set => SetInterface(WlanIntfOpcode.BssType, value);
        }

        /// <summary>
        /// Gets the state of the interface.
        /// </summary>
        /// <key>The state of the interface.</key>
        public WlanInterfaceState State => (WlanInterfaceState)QueryInterface(WlanIntfOpcode.InterfaceState);

        /// <summary>
        /// Gets the channel.
        /// </summary>
        /// <key>The channel.</key>
        /// <remarks>Not supported on Windows XP SP2.</remarks>
        public int Channel => (int)QueryInterface(WlanIntfOpcode.ChannelNumber);

        /// <summary>
        /// Gets the RSSI.
        /// </summary>
        /// <key>The RSSI.</key>
        /// <remarks>Not supported on Windows XP SP2.</remarks>
        public int Rssi => (int)QueryInterface(WlanIntfOpcode.Rssi);

        /// <summary>
        /// Gets the current operation mode.
        /// </summary>
        /// <key>The current operation mode.</key>
        /// <remarks>Not supported on Windows XP SP2.</remarks>
        public Dot11OperationMode CurrentOperationMode => (Dot11OperationMode)QueryInterface(WlanIntfOpcode.CurrentOperationMode);

        /// <summary>
        /// Gets the attributes of the current connection.
        /// </summary>
        /// <key>The current connection attributes.</key>
        /// <exception cref="Win32Exception">An exception with code 0x0000139F (The group or resource is not in the correct state to perform the requested operation.) will be thrown if the interface is not connected to a network.</exception>
        public WlanConnectionAttributes CurrentConnection
        {
            get
            {
                uint valueSize;
                WlanOpcodeValueType opcodeValueType;
                Util.ThrowIfError(NativeMethods.WlanQueryInterface(_client.ClientHandle, Guid, WlanIntfOpcode.CurrentConnection, IntPtr.Zero, out valueSize, out var valuePtr, out opcodeValueType));
                try
                {
                    return (WlanConnectionAttributes)Marshal.PtrToStructure(valuePtr, typeof(WlanConnectionAttributes));
                }
                finally
                {
                    NativeMethods.WlanFreeMemory(valuePtr);
                }
            }
        }

        /// <summary>
        /// Gets the network interface of this wireless interface.
        /// </summary>
        /// <remarks>
        /// The network interface allows querying of generic network properties such as the interface's IP address.
        /// </remarks>
        public NetworkInterface NetworkInterface
        {
            get
            {
                // Do not cache the NetworkInterface; We need it fresh
                // each time cause otherwise it caches the IP information.
                foreach (NetworkInterface netIface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    Guid netIfaceGuid = new Guid(netIface.Id);
                    if (netIfaceGuid.Equals(Guid))
                    {
                        return netIface;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Gets network interface name.
        /// </summary>
        public string Name => NetworkInterface.Name;

        /// <summary>
        /// Gets network interface description.
        /// </summary>
        public string Description => NetworkInterface.Description;

        // CONSTRUCTORS ===========================================================

        private WlanInterface(WlanClient client, WlanInterfaceInfo info)
        {
            this._client = client;
            Guid = info.Guid;
            //UpdateInfo(info);
        }

        /// <summary>
        /// Creates an instance of an interface control class.
        /// </summary>
        /// <param name="client">Native Wi-Fi client control class.</param>
        /// <param name="info">Interface information provided by client control class.</param>
        /// <returns>Instance of an interface control class.</returns>
        public static WlanInterface CreateInterface(WlanClient client, WlanInterfaceInfo info)
        {
            WlanInterface intf = new WlanInterface(client, info);
            return intf;
        }

        //internal void UpdateInfo(WlanInterfaceInfo info) {
        //    if (Guid == info.Guid) {
        //        Description = info.Description;
        //    }
        //}

        // METHODS ================================================================

        /// <summary>
        /// Disconnects this interface.
        /// </summary>
        public void Disconnect()
        {
            Util.ThrowIfError(NativeMethods.WlanDisconnect(_client.ClientHandle, Guid, IntPtr.Zero));
        }

        /// <summary>
        /// Requests a scan for available networks using this interface.
        /// </summary>
        public void Scan()
        {
            Util.ThrowIfError(NativeMethods.WlanScan(_client.ClientHandle, Guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        }

        /// <summary>
        /// Attempts to connect using specified parameters. Method returns immediately. Progress is reported through <see cref="AcmConnectionCompleted"/> event.
        /// </summary>
        /// <param name="conParam">Structure containing connection parameters.</param>
        /// <remarks><paramref name="conParam"/> must be prepared beforehand using <see cref="CreateConnectionParameters"/> and released afterwards using <see cref="DestroyConnectionParameters"/>.</remarks>
        private void Connect(WlanConnectionParameters connectionParams)
        {
            Util.ThrowIfError(NativeMethods.WlanConnect(_client.ClientHandle, Guid, ref connectionParams, IntPtr.Zero));
        }

        /// <summary>
        /// Attempts to connect using specified parameters. Method returns immediately. Progress is reported through <see cref="AcmConnectionCompleted"/> event.
        /// </summary>
        /// <param name="mode">
        /// <para>Value that specifies the mode of connection.</para>
        /// <para>Windows XP with SP3 and Wireless LAN API for Windows XP with SP2:  Only the <see cref="WlanConnectionMode.Profile"/> value is supported.</para>
        /// </param>
        /// <param name="profile">
        /// <para>Specifies the profile being used for the connection.</para>
        /// <para>If <paramref name="mode"/> is set to <see cref="WlanConnectionMode.Profile"/>, then specifies the name of the profile used for the connection.
        /// If <paramref name="mode"/> is set to <see cref="WlanConnectionMode.TemporaryProfile"/>, then specifies the XML representation of the profile used for the connection.
        /// If <paramref name="mode"/> is set to <see cref="WlanConnectionMode.DiscoverySecure"/> or <see cref="WlanConnectionMode.Unsecure"/>, then should be set to <c>null</c>.</para>
        /// </param>
        /// <param name="bssids">List of basic service set (BSS) identifiers desired for the connection.</param>
        /// <param name="bssType">Value that indicates the BSS type of the network. If a profile is provided, this BSS type must be the same as the one in the profile.</param>
        /// <param name="ssid">Structure that specifies the SSID of the network to connect to. This parameter is optional. When set to <c>null</c>, all SSIDs in the profile will be tried. This parameter must not be <c>null</c> if <paramref name="mode"/> is set to <see cref="WlanConnectionMode.DiscoverySecure"/> or <see cref="WlanConnectionMode.DiscoveryUnsecure"/>.</param>
        /// <param name="flags">Windows XP with SP3 and Wireless LAN API for Windows XP with SP2:  This member must be set to 0.</param>
        public void Connect(WlanConnectionMode mode, string profile, PhysicalAddress[] bssids, Dot11BssType bssType, Dot11Ssid? ssid, WlanConnectionFlags flags)
        {
            WlanConnectionParameters cp = new WlanConnectionParameters();
            try
            {
                cp = CreateConnectionParameters(mode, profile, bssids, bssType, ssid, flags);
                Connect(cp);
            }
            finally
            {
                DestroyConnectionParameters(cp);
            }
        }

        /// <summary>
        /// Attempts to connect using specified parameters. Method returns immediately. Progress is reported through <see cref="AcmConnectionCompleted"/> event.
        /// </summary>
        /// <param name="profile">
        /// If <paramref name="temporary"/> is set to <c>true</c> this field contains profile XML.
        /// If <paramref name="temporary"/> is set to <c>false</c> this field contains profile name.
        /// </param>
        /// <param name="temporary">Specifies whether to use a temporary or saved profile.</param>
        public void Connect(string profile, bool temporary)
        {
            WlanConnectionMode mode = temporary ? WlanConnectionMode.TemporaryProfile : WlanConnectionMode.Profile;
            Connect(mode, profile, null, Dot11BssType.Any, null, 0);
        }

        /// <summary>
        /// Attempts to connect using specified parameters. Method returns immediately. Progress is reported through <see cref="AcmConnectionCompleted"/> event.
        /// </summary>
        /// <param name="ssid">SSID of a network to connect.</param>
        /// <param name="discoverySecure">Whether to use secure discovery.</param>
        public void Connect(Dot11Ssid ssid, bool discoverySecure)
        {
            WlanConnectionMode mode = discoverySecure ? WlanConnectionMode.DiscoverySecure : WlanConnectionMode.DiscoveryUnsecure;
            Connect(mode, null, null, Dot11BssType.Any, ssid, 0);
        }

        public WlanReasonCode EditProfile(string profileName)
        {
            Util.ThrowIfError(NativeMethods.WlanUIEditProfile(1, profileName, Guid, IntPtr.Zero, WlDisplayPages.WlConnectionPage, IntPtr.Zero, out var reasonCode));
            return reasonCode;
        }

        /// <summary>
        /// Deletes a profile.
        /// </summary>
        /// <param name="profileName">
        /// The name of the profile to be deleted. Profile names are case-sensitive.
        /// On Windows XP SP2, the supplied name must match the profile name derived automatically from the Ssid of the network. For an infrastructure network profile, the Ssid must be supplied for the profile name. For an ad hoc network profile, the supplied name must be the Ssid of the ad hoc network followed by <c>-adhoc</c>.
        /// </param>
        public void DeleteProfile(string profileName)
        {
            Util.ThrowIfError(NativeMethods.WlanDeleteProfile(_client.ClientHandle, Guid, profileName, IntPtr.Zero));
        }

        /// <summary>
        /// Sets the profile.
        /// </summary>
        /// <param name="flags">The flags to set on the profile.</param>
        /// <param name="profileXml">The XML representation of the profile. On Windows XP SP 2, special care should be taken to adhere to its limitations.</param>
        /// <param name="overwrite">If a profile by the given name already exists, then specifies whether to overwrite it (if <c>true</c>) or return an error (if <c>false</c>).</param>
        /// <returns>The resulting code indicating a success or the reason why the profile wasn't valid.</returns>
        public WlanReasonCode SetProfileXml(WlanProfileFlags flags, string profileXml, bool overwrite)
        {
            Util.ThrowIfError(
                NativeMethods.WlanSetProfile(_client.ClientHandle, Guid, flags, profileXml, null, overwrite, IntPtr.Zero, out var reasonCode));
            return reasonCode;
        }

        /// <summary>
        /// Gets the profile's XML specification.
        /// </summary>
        /// <param name="profileName">The name of the profile.</param>
        /// <returns>The XML document.</returns>
        public string GetProfileXml(string profileName)
        {
            WlanProfileFlags flags;
            WlanAccess access;
            Util.ThrowIfError(
                NativeMethods.WlanGetProfile(_client.ClientHandle, Guid, profileName, IntPtr.Zero, out var profileXmlPtr, out flags, out access));
            try
            {
                return Marshal.PtrToStringUni(profileXmlPtr);
            }
            finally
            {
                NativeMethods.WlanFreeMemory(profileXmlPtr);
            }
        }

        // SYNCHRONOUS METHODS ===================================================

        /// <summary>
        /// Attempts to connect using specified parameters. Method returns after successful connection has been established or on timeout.
        /// </summary>
        /// <param name="profile">
        /// If <paramref name="temporary"/> is set to <c>true</c> this field contains profile XML.
        /// If <paramref name="temporary"/> is set to <c>false</c> this field contains profile name.
        /// </param>
        /// <param name="temporary">Specifies whether to use a temporary or saved profile.</param>
        /// <returns>Value indicating whether connection attempt finished successfully within specified period of time.</returns>
        public bool ConnectSync(string profile, bool temporary, int timeout)
        {
            WlanConnectionMode mode = temporary ? WlanConnectionMode.TemporaryProfile : WlanConnectionMode.Profile;
            return ConnectSync(mode, profile, null, Dot11BssType.Any, null, 0, timeout);
        }

        /// <summary>
        /// Attempts to connect using specified parameters. Method returns after successful connection has been established or on timeout.
        /// </summary>
        /// <param name="ssid">SSID of a network to connect.</param>
        /// <param name="discoverySecure">Whether to use secure discovery.</param>
        /// <returns>Value indicating whether connection attempt finished successfully within specified period of time.</returns>
        public bool ConnectSync(Dot11Ssid ssid, bool discoverySecure, int timeout)
        {
            WlanConnectionMode mode = discoverySecure ? WlanConnectionMode.DiscoverySecure : WlanConnectionMode.DiscoveryUnsecure;
            return ConnectSync(mode, null, null, Dot11BssType.Any, ssid, 0, timeout);
        }

        /// <summary>
        /// Attempts to connect using specified parameters. Method returns after successful connection has been established or on timeout.
        /// </summary>
        /// <param name="mode">
        /// <para>Value that specifies the mode of connection.</para>
        /// <para>Windows XP with SP3 and Wireless LAN API for Windows XP with SP2:  Only the <see cref="WlanConnectionMode.Profile"/> value is supported.</para>
        /// </param>
        /// <param name="profile">
        /// <para>Specifies the profile being used for the connection.</para>
        /// <para>If <paramref name="mode"/> is set to <see cref="WlanConnectionMode.Profile"/>, then specifies the name of the profile used for the connection.
        /// If <paramref name="mode"/> is set to <see cref="WlanConnectionMode.TemporaryProfile"/>, then specifies the XML representation of the profile used for the connection.
        /// If <paramref name="mode"/> is set to <see cref="WlanConnectionMode.DiscoverySecure"/> or <see cref="WlanConnectionMode.Unsecure"/>, then should be set to <c>null</c>.</para>
        /// </param>
        /// <param name="bssids">List of basic service set (BSS) identifiers desired for the connection.</param>
        /// <param name="bssType">Value that indicates the BSS type of the network. If a profile is provided, this BSS type must be the same as the one in the profile.</param>
        /// <param name="ssid">Structure that specifies the SSID of the network to connect to. This parameter is optional. When set to <c>null</c>, all SSIDs in the profile will be tried. This parameter must not be <c>null</c> if <paramref name="mode"/> is set to <see cref="WlanConnectionMode.DiscoverySecure"/> or <see cref="WlanConnectionMode.DiscoveryUnsecure"/>.</param>
        /// <param name="flags">Windows XP with SP3 and Wireless LAN API for Windows XP with SP2:  This member must be set to 0.</param>
        /// <returns>Value indicating whether connection attempt finished successfully within specified period of time.</returns>
        public bool ConnectSync(WlanConnectionMode mode, string profile, PhysicalAddress[] bssids, Dot11BssType bssType, Dot11Ssid? ssid, WlanConnectionFlags flags, int timeout)
        {
            WlanConnectionParameters cp = new WlanConnectionParameters();
            bool value;
            try
            {
                cp = CreateConnectionParameters(mode, profile, bssids, bssType, ssid, flags);
                value = ConnectSync(cp, timeout);
            }
            finally
            {
                DestroyConnectionParameters(cp);
            }

            return value;
        }

        /// <summary>
        /// Attempts to connect using specified parameters. Method returns after successful connection has been established or on timeout.
        /// </summary>
        /// <param name="conParam">Structure containing connection parameters.</param>
        /// <param name="timeout">Timeout after which method returns unsuccessfully.</param>
        /// <returns>Value indicating whether connection attempt finished successfully within specified period of time.</returns>
        /// <remarks><paramref name="conParam"/> must be prepared beforehand using <see cref="CreateConnectionParameters"/> and released afterwards using <see cref="DestroyConnectionParameters"/>.</remarks>
        private bool ConnectSync(WlanConnectionParameters conParam, int timeout)
        {
            Object key = new Object();
            bool value = false;
            bool quit = false;

            AcmConnectionEventHandler successHandler = (sender, e) =>
            {
                lock (key)
                {
                    if (!quit)
                    {
                        //TODO if profile name equals
                        value = true;
                        quit = true;
                        Monitor.Pulse(key);
                    }
                }
            };

            AcmConnectionEventHandler failureHandler = (sender, e) =>
            {
                lock (key)
                {
                    if (!quit)
                    {
                        //TODO if profile name equals
                        value = false;
                        quit = true;
                        Monitor.Pulse(key);
                    }
                }
            };

            System.Timers.ElapsedEventHandler timerHandler = (sender, e) =>
            {
                lock (key)
                {
                    quit = true;
                    Monitor.Pulse(key);
                }
            };

            System.Timers.Timer timer = new System.Timers.Timer(timeout);
            timer.AutoReset = false;
            timer.Elapsed += timerHandler;

            try
            {
                lock (key)
                {
                    AcmConnectionCompleted += successHandler;
                    AcmConnectionAttemptFailed += failureHandler;
                    timer.Start();
                    Connect(conParam);
                    while (!quit)
                        Monitor.Wait(key);
                }
            }
            finally
            {
                timer.Stop();
                AcmConnectionCompleted -= successHandler;
                AcmConnectionAttemptFailed -= failureHandler;
            }

            return value;
        }

        // INTERNALS ==============================================================

        /// <summary>
        /// Method creates a valid structure containing connection parameters for use in native API calls.
        /// </summary>
        /// <returns>Structure that specifies the parameters used when using the <see cref="Connect"/> method.</returns>
        /// <remarks>
        /// <para>Wrap call to this function in try-catch-finally. Place <see cref="DestroyConnectionParameters"/> in finally block.</para>
        /// <para>Parameter validity is described in <see cref="Connect"/> and <see cref="ConnectSync"/> methods.</para>
        /// </remarks>
        private static WlanConnectionParameters CreateConnectionParameters(WlanConnectionMode mode, string profile, PhysicalAddress[]? bssids, Dot11BssType bssType, Dot11Ssid? ssid,
            WlanConnectionFlags flags)
        {
            WlanConnectionParameters cp = new WlanConnectionParameters();
            cp.BssType = bssType;
            cp.ConnectionMode = mode;
            cp.Flags = flags;
            cp.Profile = profile;

            Dot11BssidList bssidList = new Dot11BssidList();
            if (bssids != null)
            {
                Dot11MacAddress[] macs = Util.ConvertPhysicalAddresses(bssids);
                bssidList = Dot11BssidList.Build(macs);
                cp.DesiredBssidList = Marshal.AllocHGlobal(bssidList.Header.Size);
                Int64 address = cp.DesiredBssidList.ToInt64();
                Marshal.StructureToPtr(bssidList.Header, new IntPtr(address), false);
                address += Marshal.SizeOf(typeof(NdisObjectHeader));
                Marshal.StructureToPtr(bssidList.ListHeader, new IntPtr(address), false);
                address += Marshal.SizeOf(typeof(Dot11BssidListHeader));
                Int64 offset = Marshal.SizeOf(typeof(Dot11MacAddress));
                for (int i = 0; i < bssidList.Entries.Length; i++)
                {
                    Marshal.StructureToPtr(bssidList.Entries[i], new IntPtr(address), false);
                    address += offset;
                }
            }

            if (ssid.HasValue)
            {
                cp.Ssid = Marshal.AllocHGlobal(Marshal.SizeOf(ssid.Value));
                Marshal.StructureToPtr(ssid.Value, cp.Ssid, false);
            }

            return cp;
        }

        /// <summary>
        /// Releases unmanaged resources held by <see cref="WlanConnectionParameters"/> object.
        /// </summary>
        /// <param name="cp">Structure whose resources will be released.</param>
        /// <remarks>Call to this method should be placed in a finally block.</remarks>
        private static void DestroyConnectionParameters(WlanConnectionParameters cp)
        {
            if (cp.DesiredBssidList != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(cp.DesiredBssidList);
                cp.DesiredBssidList = IntPtr.Zero;
            }

            if (cp.Ssid != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(cp.Ssid);
                cp.Ssid = IntPtr.Zero;
            }
        }

        private void SetInterface(WlanIntfOpcode opCode, Object value)
        {
            IntPtr data;
            int dataSize = Marshal.SizeOf(value);
            data = Marshal.AllocHGlobal(dataSize);
            switch (opCode)
            {
                case WlanIntfOpcode.AutoconfEnabled:
                case WlanIntfOpcode.BackgroundScanEnabled:
                case WlanIntfOpcode.MediaStreamingMode:
                case WlanIntfOpcode.CurrentOperationMode:
                //Marshal.WriteInt32(data, Convert.ToInt32((uint)value));
                //break;
                case WlanIntfOpcode.BssType:
                    Marshal.WriteInt32(data, (int)value);
                    break;
                case WlanIntfOpcode.RadioState:
                    Marshal.StructureToPtr(value, data, false);
                    break;
                default:
                    throw new NotSupportedException();
            }

            try
            {
                Util.ThrowIfError(NativeMethods.WlanSetInterface(_client.ClientHandle, Guid, opCode, (uint)dataSize, data, IntPtr.Zero));
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }

        private Object QueryInterface(WlanIntfOpcode opCode)
        {
            uint dataSize;
            WlanOpcodeValueType opcodeValueType;
            Util.ThrowIfError(NativeMethods.WlanQueryInterface(_client.ClientHandle, Guid, opCode, IntPtr.Zero, out dataSize, out var data, out opcodeValueType));
            try
            {
                switch (opCode)
                {
                    case WlanIntfOpcode.AutoconfEnabled:
                    case WlanIntfOpcode.BackgroundScanEnabled:
                    case WlanIntfOpcode.MediaStreamingMode:
                    case WlanIntfOpcode.SupportedSafeMode:
                    case WlanIntfOpcode.CertifiedSafeMode:
                        return Convert.ToBoolean(Marshal.ReadByte(data));
                    case WlanIntfOpcode.ChannelNumber:
                    case WlanIntfOpcode.CurrentOperationMode:
                    // type to uint
                    case WlanIntfOpcode.Rssi:
                    case WlanIntfOpcode.BssType:
                    case WlanIntfOpcode.InterfaceState:
                        return Marshal.ReadInt32(data);
                    case WlanIntfOpcode.RadioState:
                        return Marshal.PtrToStructure(data, typeof(WlanRadioState));
                    case WlanIntfOpcode.CurrentConnection:
                        return Marshal.PtrToStructure(data, typeof(WlanConnectionAttributes));
                    case WlanIntfOpcode.SupportedInfrastructureAuthCipherPairs:
                    case WlanIntfOpcode.SupportedAdhocAuthCipherPairs:
                    case WlanIntfOpcode.SupportedCountryOrRegionStringList:
                    case WlanIntfOpcode.Statistics:
                    default:
                        throw new NotSupportedException();
                }
            }
            finally
            {
                NativeMethods.WlanFreeMemory(data);
            }
        }

        // EVENTS =================================================================

        #region Interface specific Events

        public event WlanEventHandler? WlanNotification;

        internal void OnWlanNotification(WlanEventArgs notification)
        {
            WlanNotification?.Invoke(this, notification);
        }

        public event OneXResultUpdateEventHandler? OneXResultUpdated;
        public event OneXAuthRestartedEventHandler? OneXAuthRestarted;

        internal void OnOneXResultUpdated(OneXResultUpdateEventArgs e)
        {
            OneXResultUpdated?.Invoke(this, e);
        }

        internal void OnOneXAuthRestarted(OneXResultUpdateEventArgs e)
        {
            OneXAuthRestarted?.Invoke(this, e);
        }

        public event AcmEventHandler? AcmNetworkNotFound;
        public event AcmEventHandler? AcmNetworkFound;
        public event AcmEventHandler? AcmProfileUnblocked;
        public event AcmEventHandler? AcmProfileBlocked;
        public event AcmBooleanEventHandler? AcmScreenPowerChanged;
        public event AcmBssTypeChangeEventHandler? AcmBssTypeChanged;
        public event AcmPowerSettingChangeEventHandler? AcmPowerSettingChanged;
        public event AcmReasonCodeEventHandler? AcmScanFailed;
        public event AcmConnectionEventHandler? AcmConnectionStarted;
        public event AcmConnectionEventHandler? AcmConnectionCompleted;
        public event AcmConnectionEventHandler? AcmConnectionAttemptFailed;
        public event AcmProfileNameChangeEventHandler? AcmProfileNameChanged;
        public event AcmEventHandler? AcmProfilesExhausted;
        public event AcmAdhocNetworkStateChange? AcmAdhocNetworkStateChanged;
        public event AcmConnectionEventHandler? AcmDisconnecting;
        public event AcmConnectionEventHandler? AcmDisconnected;
        public event AcmEventHandler? AcmScanCompleted;
        public event AcmEventHandler? AcmScanListRefreshed;

        internal void OnAcmNetworkNotFound(AcmEventArgs e)
        {
            AcmNetworkNotFound?.Invoke(this, e);
        }

        internal void OnAcmNetworkFound(AcmEventArgs e)
        {
            AcmNetworkFound?.Invoke(this, e);
        }

        internal void OnAcmProfileUnblocked(AcmEventArgs e)
        {
            AcmProfileUnblocked?.Invoke(this, e);
        }

        internal void OnAcmProfileBlocked(AcmEventArgs e)
        {
            AcmProfileBlocked?.Invoke(this, e);
        }

        internal void OnAcmScreenPowerChanged(AcmBooleanEventArgs e)
        {
            AcmScreenPowerChanged?.Invoke(this, e);
        }

        internal void OnAcmBssTypeChanged(AcmBssTypeChangeEventArgs e)
        {
            AcmBssTypeChanged?.Invoke(this, e);
        }

        internal void OnAcmPowerSettingChanged(AcmPowerSettingChangeEventArgs e)
        {
            AcmPowerSettingChanged?.Invoke(this, e);
        }

        internal void OnAcmScanFailed(AcmReasonCodeEventArgs e)
        {
            AcmScanFailed?.Invoke(this, e);
        }

        internal void OnAcmConnectionStarted(AcmConnectionEventArgs e)
        {
            AcmConnectionStarted?.Invoke(this, e);
        }

        internal void OnAcmConnectionCompleted(AcmConnectionEventArgs e)
        {
            AcmConnectionCompleted?.Invoke(this, e);
        }

        internal void OnAcmConnectionAttemptFailed(AcmConnectionEventArgs e)
        {
            AcmConnectionAttemptFailed?.Invoke(this, e);
        }

        internal void OnAcmProfileNameChanged(AcmProfileNameChangeEventArgs e)
        {
            AcmProfileNameChanged?.Invoke(this, e);
        }

        internal void OnAcmProfilesExhausted(AcmEventArgs e)
        {
            AcmProfilesExhausted?.Invoke(this, e);
        }

        internal void OnAcmAdhocNetworkStateChanged(AcmAdhocNetworkStateChangeEventArgs e)
        {
            AcmAdhocNetworkStateChanged?.Invoke(this, e);
        }

        internal void OnAcmDisconnecting(AcmConnectionEventArgs e)
        {
            AcmDisconnecting?.Invoke(this, e);
        }

        internal void OnAcmDisconnected(AcmConnectionEventArgs e)
        {
            AcmDisconnected?.Invoke(this, e);
        }

        internal void OnAcmScanCompleted(AcmEventArgs e)
        {
            AcmScanCompleted?.Invoke(this, e);
        }

        internal void OnAcmScanListRefreshed(AcmEventArgs e)
        {
            AcmScanListRefreshed?.Invoke(this, e);
        }

        public event MsmNotificationEventHandler? MsmAssociating;
        public event MsmNotificationEventHandler? MsmAssociated;
        public event MsmNotificationEventHandler? MsmAuthenticating;
        public event MsmNotificationEventHandler? MsmConnected;
        public event MsmNotificationEventHandler? MsmRoamingStarted;
        public event MsmNotificationEventHandler? MsmRoamingEnded;
        public event MsmRadioStateChangeEventHandler? MsmRadioStateChanged;
        public event MsmDwordEventHandler? MsmSignalQualityChanged;
        public event MsmNotificationEventHandler? MsmDisassociating;
        public event MsmNotificationEventHandler? MsmDisconnected;
        public event MsmNotificationEventHandler? MsmPeerJoined;
        public event MsmNotificationEventHandler? MsmPeerLeft;
        public event MsmDwordEventHandler? MsmAdapterOperationModeChanged;

        internal void OnMsmAssociating(MsmNotificationEventArgs e)
        {
            MsmAssociating?.Invoke(this, e);
        }

        internal void OnMsmAssociated(MsmNotificationEventArgs e)
        {
            MsmAssociated?.Invoke(this, e);
        }

        internal void OnMsmAuthenticating(MsmNotificationEventArgs e)
        {
            MsmAuthenticating?.Invoke(this, e);
        }

        internal void OnMsmConnected(MsmNotificationEventArgs e)
        {
            MsmConnected?.Invoke(this, e);
        }

        internal void OnMsmRoamingStarted(MsmNotificationEventArgs e)
        {
            MsmRoamingStarted?.Invoke(this, e);
        }

        internal void OnMsmRoamingEnded(MsmNotificationEventArgs e)
        {
            MsmRoamingEnded?.Invoke(this, e);
        }

        internal void OnMsmRadioStateChange(MsmRadioStateChangeEventArgs e)
        {
            MsmRadioStateChanged?.Invoke(this, e);
        }

        internal void OnMsmSignalQualityChanged(MsmDwordEventArgs e)
        {
            MsmSignalQualityChanged?.Invoke(this, e);
        }

        internal void OnMsmDisassociating(MsmNotificationEventArgs e)
        {
            MsmDisassociating?.Invoke(this, e);
        }

        internal void OnMsmDisconnected(MsmNotificationEventArgs e)
        {
            MsmDisconnected?.Invoke(this, e);
        }

        internal void OnMsmPeerJoined(MsmNotificationEventArgs e)
        {
            MsmPeerJoined?.Invoke(this, e);
        }

        internal void OnMsmPeerLeft(MsmNotificationEventArgs e)
        {
            MsmPeerLeft?.Invoke(this, e);
        }

        internal void OnMsmAdapterOperationModeChanged(MsmDwordEventArgs e)
        {
            MsmAdapterOperationModeChanged?.Invoke(this, e);
        }

        #endregion Interface specific Events
    }
}