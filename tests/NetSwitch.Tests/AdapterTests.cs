using NetSwitch.Model;
using NetSwitch.Windows;
using Xunit;

namespace NetSwitch.Tests
{
    public class AdapterTests
    {
        [Theory]
        [InlineData(@"PCI\VEN_8086&DEV_15F3&SUBSYS_86721043&REV_03\3&11583659&0&E0")]
        [InlineData(@"USB\VID_0BDA&PID_8153\000001")]
        // Сетевая карта гостевой системы Hyper-V: для самой системы она настоящая
        [InlineData(@"VMBUS\{F8615163-DF3E-46C5-913F-F2D2F965ED0E}\{12345678-0000-0000-0000-000000000000}")]
        [InlineData(@"SD\VID_02D0&PID_4330\1")]
        public void Device_on_a_hardware_bus_is_physical(string pnpDeviceId)
        {
            Assert.True(AdapterClassifier.IsPhysical(pnpDeviceId));
        }

        [Theory]
        [InlineData(@"ROOT\VMS_MP\0000")]
        [InlineData(@"ROOT\NET\0000")]
        [InlineData(@"root\vmware\0000")]
        [InlineData(@"SWD\MSRRAS\MS_AGILEVPNMINIPORT")]
        [InlineData(@"BTH\MS_BTHPAN\6&2A9B5A5C&0&2")]
        [InlineData(@"{5D624F94-8850-40C3-A3FA-A4FD2080BAF3}\VWIFIMP_WFD\5&3A1A9B8&0&11")]
        [InlineData("")]
        [InlineData(null)]
        public void Software_and_bluetooth_devices_are_virtual(string? pnpDeviceId)
        {
            Assert.False(AdapterClassifier.IsPhysical(pnpDeviceId));
        }

        [Theory]
        [InlineData(2, 0, AdapterState.Connected)]
        [InlineData(7, 0, AdapterState.NoLink)]
        [InlineData(0, 22, AdapterState.Disabled)]
        [InlineData(0, 0, AdapterState.Disabled)]
        [InlineData(5, 0, AdapterState.Disabled)]
        [InlineData(4, 0, AdapterState.Unavailable)]
        [InlineData(6, 0, AdapterState.Unavailable)]
        [InlineData(1, 0, AdapterState.Connecting)]
        [InlineData(9, 0, AdapterState.Connecting)]
        // Признак выключенного устройства важнее состояния соединения
        [InlineData(7, 22, AdapterState.Disabled)]
        public void Maps_wmi_status_to_state(int connectionStatus, int deviceError, object expected)
        {
            Assert.Equal(expected, AdapterReader.StateFrom(connectionStatus, deviceError));
        }

        [Fact]
        public void Adapter_without_status_is_unavailable()
        {
            Assert.Equal(AdapterState.Unavailable, AdapterReader.StateFrom(null, null));
        }

        [Fact]
        public void Builds_adapter_from_wmi_row()
        {
            var row = new WmiRow
            {
                { "GUID", "{0A1B2C3D-0000-0000-0000-000000000001}" },
                { "NetConnectionID", "Ethernet" },
                { "Name", "Intel(R) Ethernet Controller I225-V" },
                { "PNPDeviceID", @"PCI\VEN_8086&DEV_15F3\3&11583659&0&E0" },
                { "MACAddress", "00:11:22:33:44:55" },
                { "NetConnectionStatus", (ushort)2 },
                { "ConfigManagerErrorCode", (uint)0 }
            };

            Adapter adapter = AdapterReader.FromWmi(row);

            Assert.Equal("{0A1B2C3D-0000-0000-0000-000000000001}", adapter.Id);
            Assert.Equal("Ethernet", adapter.Name);
            Assert.Equal("Intel(R) Ethernet Controller I225-V", adapter.Description);
            Assert.Equal("00:11:22:33:44:55", adapter.MacAddress);
            Assert.True(adapter.IsPhysical);
            Assert.Equal(AdapterState.Connected, adapter.State);
        }

        [Fact]
        public void Wmi_row_with_missing_values_still_gives_an_adapter()
        {
            var row = new WmiRow { { "NetConnectionID", "Ethernet 2" }, { "GUID", null }, { "MACAddress", null } };

            Adapter adapter = AdapterReader.FromWmi(row);

            Assert.Equal("Ethernet 2", adapter.Name);
            Assert.Equal("", adapter.Id);
            Assert.False(adapter.IsPhysical);
            Assert.Equal(AdapterState.Unavailable, adapter.State);
        }
    }
}
