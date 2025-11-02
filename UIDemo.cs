using System;
using System.Collections.Generic;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter
{
    public static class UIDemo
    {
        public static void RunDemo()
        {
            var mockStatus = new
            {
                Status = "Connected",
                Ssid = "CompanyWiFi",
                Signal = 85,
                IpAddress = "192.168.1.100",
                MacAddress = "00:1B:44:11:3A:B7",
                Band = "5GHz",
                Channel = "36",
                Authentication = "WPA3-Enterprise"
            };

            // Since ShowNetworkStatus method uses NetworkOperations.ConnectionStatus,
            // we'll create a simple table display instead
            var statusItems = new List<(string label, string value, ConsoleColor? color)>
            {
                ("Status", mockStatus.Status, UIHelper.Colors.Success),
                ("Network", mockStatus.Ssid, null),
                ("Signal", $"{mockStatus.Signal}% {UIHelper.GetSignalStrengthBar(mockStatus.Signal)}", UIHelper.GetSignalColor(mockStatus.Signal)),
                ("IP Address", mockStatus.IpAddress, null),
                ("MAC Address", mockStatus.MacAddress, UIHelper.Colors.TextSubtle),
                ("Band", mockStatus.Band, null),
                ("Channel", mockStatus.Channel, null),
                ("Security", mockStatus.Authentication, mockStatus.Authentication.Contains("WPA3") ? UIHelper.Colors.Success : UIHelper.Colors.Warning)
            };
            UIHelper.PrintBox("Network Status", statusItems);

            // 7. Interactive menu
            Console.WriteLine("\n6. Interactive Menu:");
            var options = new List<string>
            {
                "Scan for networks",
                "Connect to network",
                "View connection status",
                "Manage saved networks",
                "View logs",
                "Exit"
            };

            var choice = UIHelper.ShowMenu("Main Menu", options, "Choose an option (1-6)");
            if (choice >= 0 && choice < options.Count)
            {
                UIHelper.ShowInlineMessage($"Selected: {options[choice]}", UIHelper.MessageType.Info);
            }

            // 8. Confirmation dialog
            Console.WriteLine("\n7. Confirmation Dialog:");
            if (UIHelper.Confirm("Do you want to save these settings?", true))
            {
                UIHelper.ShowInlineMessage("Settings saved successfully", UIHelper.MessageType.Success);
            }
            else
            {
                UIHelper.ShowInlineMessage("Settings not saved", UIHelper.MessageType.Warning);
            }

            Console.WriteLine("\n=== Demo Complete ===");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
