﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxns.Rssdp.Infrastructure
{
	/// <summary>
	/// Provides constants for common values related to the SSDP protocols.
	/// </summary>
	public static class SsdpConstants
	{
		/// <summary>
		/// Multicast IPV6 Address used for SSDP multicast messages. Value is FF02::C.
		/// </summary>
		public const string MulticastLinkLocalAddressV6 = "FF02::C";  //(IPv6 link-local)
		/// <summary>
		/// Multicast IP Address used for SSDP multicast messages. Value is 239.255.255.250.
		/// </summary>
		public const string MulticastLocalAdminAddress = "239.255.255.250";
		/// <summary>
		/// The UDP port used for SSDP multicast messages. Values is 1900.
		/// </summary>
		public const int MulticastPort = 1900;
		/// <summary>
		/// The default multicase TTL for SSDP multicast messages. Value is 4.
		/// </summary>
		public const int SsdpDefaultMulticastTimeToLive = 4;

		internal const string MSearchMethod = "M-SEARCH";

		internal const string SsdpDiscoverMessage = "ssdp:discover";
		internal const string SsdpDiscoverAllSTHeader = "ssdp:all";

		internal const string SsdpDeviceDescriptionXmlNamespace = "urn:schemas-upnp-org:device-1-0";

		/// <summary>
		/// Default buffer size for receiving SSDP broadcasts. Value is 8192 (bytes).
		/// </summary>
		public const int DefaultUdpSocketBufferSize = 8192;
		/// <summary>
		/// The maximum possible buffer size for a UDP message. Value is 65507 (bytes).
		/// </summary>
		public const int MaxUdpSocketBufferSize = 65507; // Max possible UDP packet size on IPv4 without using 'jumbograms'.

		/// <summary>
		/// Namespace/prefix for UPnP device types. Values is schemas-upnp-org.
		/// </summary>
		public const string UpnpDeviceTypeNamespace = "schemas-upnp-org";
		/// <summary>
		/// UPnP Root Device type. Value is upnp:rootdevice.
		/// </summary>
		public const string UpnpDeviceTypeRootDevice = "upnp:rootdevice";
		/// <summary>
		/// The value is used by Windows Explorer for device searches instead of the UPNPDeviceTypeRootDevice constant. 
		/// Not sure why (different spec, bug, alternate protocol etc). Used to enable Windows Explorer support.
		/// </summary>
		public const string PnpDeviceTypeRootDevice = "pnp:rootdevice";
		/// <summary>
		/// UPnP Basic Device type. Value is Basic.
		/// </summary>
		public const string UpnpDeviceTypeBasicDevice = "Basic";

		internal const string SsdpKeepAliveNotification = "ssdp:alive";
		internal const string SsdpByeByeNotification = "ssdp:byebye";

		/// <summary>
		/// The default number of times to resend each UDP packet. 
		/// </summary>
		/// <remarks>
		/// <para>SSDP spec recommends sending messages multiple times (not more than 3) to account for possible packet loss over UDP.</para>
		/// <para>This constant has a value of 3.</para>
		/// </remarks>
		public const int DefaultUdpResendCount = 3;

		private static readonly TimeSpan _DefaultUdpResendDelay = TimeSpan.FromMilliseconds(100);

		/// <summary>
		/// The default time to delay between re-sends of UDP packets.
		/// </summary>
		/// <remarks>
		/// <para>This property returns a value of 100 milliseconds.</para>
		/// </remarks>
		/// <seealso cref="DefaultUdpResendCount"/>
		public static TimeSpan DefaultUdpResendDelay { get { return _DefaultUdpResendDelay; } } 
	}
}
