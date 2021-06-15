// disposition ///, /*, debug, etc.
// go through "blue" yellow red purple text colors

using HidLibrary;
using IniParser;
using IniParser.Model;
using System;
using System.Linq;

namespace VA.HidInterface
{
    public class HostInterface
    {
        #region Fields & Objects

        private HidDevice kbDevice;
        private int _vendorID;
        private int _productID;
        private int _usagePage;
        private int _usage;
        private int deviceAttachCount = 0;

        #endregion

        #region Enumeration

        // Enumeration for actions to execute by HidDevice
        public enum HidAction
        {
            ChangeLayer = 1
        }

        #endregion

        #region Properties

        // Property indicating the name of the target HidDevice
        public string DeviceName { get; private set; }

        // Property indicating if interface between HostInterface (computer) and HidDevice is active
        public bool IsActive { get; private set; }

        // Property indicating if HostInterface (computer) is connected to HidDevice
        public bool IsConnected
        {
            get
            {
                if (kbDevice?.IsConnected != true)
                    return false;
                else
                    return true;
            }
        }

        // Property indicating if HostInterface (computer) is listening for messages from HidDevice
        private bool _isListening = false;
        public bool IsListening
        {
            get { return _isListening; }
            set
            {
                if (_isListening != value)
                {
                    _isListening = value;
                    if (_isListening == true)
                    {
                        kbDevice.MonitorDeviceEvents = true; // Enable HidDevice event monitoring
                        kbDevice.ReadReport(OnReport); // Subscribe to OnReport (as callback) for next received message from HidDevice
                    }
                    else
                        kbDevice.MonitorDeviceEvents = false; // Disable HidDevice event monitoring
                }
            }
        }

        #endregion

        #region Constructor

        // Method for creating HostInterface instance
        public HostInterface(string deviceName, string vendorID, string productID, string usagePage = null, string usage = null)
        {
            // Convert passed-in hexadecimal strings (for target HidDevice) to integers
            _vendorID = ConvertHexStringToInt(vendorID);
            _productID = ConvertHexStringToInt(productID);
            if (usagePage != null)
                _usagePage = ConvertHexStringToInt(usagePage);
            else
                _usagePage = -1;
            if (usage != null)
                _usage = ConvertHexStringToInt(usage);
            else
                _usage = -1;

            // Transfer passed-in target HidDevice name
            this.DeviceName = deviceName;
        }

        #endregion

        #region Interface Methods

        // Method for connecting with a HidDevice 
        public void Connect(bool hidDeviceListeningEnabled = true, string hidConfigFilePath = null)
        {
            try // Attempt the following code...
            {
                if (_usagePage == -1 || _usage == -1) // Check if usagePage OR usage were not provided when HostInterface was instantiated
                    kbDevice = HidDevices.Enumerate(_vendorID, _productID).FirstOrDefault(); // Find first HidDevice that matches _vendorID and _productID
                else
                {
                    var devices = HidDevices.Enumerate(_vendorID, _productID, _usagePage); // Capture all HidDevices
                    ///throw new Exception("test"); // (debug)
                    foreach (HidDevice dev in devices) // Loop through each HidDevice
                    {
                        if (dev.Capabilities.Usage == _usage) // Check if current HidDevice matches target device's usage
                        {
                            kbDevice = dev; // Store the found HidDevice
                            break; // Break out of parent 'foreach' loop
                        }
                    }
                }
                if (kbDevice != null) // Check if target HidDevice was found
                {
                    ///VoiceAttackPlugin.OutputToLog(this.DeviceName + " found!", "purple"); // Output info to event log (debug)
                    kbDevice.OpenDevice(); // Open connection between HostInterface (computer) and HidDevice
                    this.IsActive = true; // Set flag indicating interface between HostInterface (computer) and HidDevice is active
                    this.IsListening = hidDeviceListeningEnabled; // Transfer passed-in listening state (and activate listening for HidDevice data messages if applicable)
                    kbDevice.Inserted += DeviceAttachedHandler; // Subscribe to HidDevice attachment events
                    kbDevice.Removed += DeviceRemovedHandler; // Subscribe to HidDevice removal events
                    VoiceAttackPlugin.OutputToLog("VoiceAttack is connected with " + this.DeviceName, "blue"); // Output info to event log
                }
                else
                    VoiceAttackPlugin.OutputToLog("Could not find '" + this.DeviceName + "' HID hardware", "red"); // Output info to event log
            }
            catch (Exception ex) // Handle exceptions encountered in above code
            {
                VoiceAttackPlugin.OutputToLog("Error connecting to " + this.DeviceName + ". " + ex.Message, "red"); // Output info to event log
                return;
            }
            try // Attempt the following code...
            {
                if (kbDevice != null && hidConfigFilePath != null)  // Check if target HidDevice was found AND a file path was passed into method
                {
                    IniData data = new IniData(); // Create new IniData instance
                    KeyDataCollection keyCol = data["HIDHardwareInfo"]; // Create new KeyDataCollection and file section name
                    keyCol["DeviceName"] = this.DeviceName; // Store HID hardware device name
                    keyCol["VendorID"] = "0x" + String.Format("{0:X4}", _vendorID); // Store HID hardware Vendor ID
                    keyCol["ProductID"] = "0x" + String.Format("{0:X4}", _productID); // Store HID hardware Product ID
                    keyCol["UsagePage"] = "0x" + String.Format("{0:X4}", _usagePage); // Store HID hardware Usage Page
                    keyCol["Usage"] = "0x" + String.Format("{0:X4}", _usage); // Store HID hardware Usage
                    var parser = new FileIniDataParser(); // Create new FileIniDataParser instance
                    ///string message = "Configuration for '" + this.DeviceName + "'" + (System.IO.File.Exists(hidConfigFilePath) == true ? " updated" : " saved"); // Construct event log message (debug)
                    parser.WriteFile(hidConfigFilePath, data); // Write stored data to ini file
                    ///VoiceAttackPlugin.OutputToLog(message, "purple"); // Output info to event log (debug)
                }
            }
            catch (Exception ex) // Handle exceptions encountered in above code
            {
                VoiceAttackPlugin.OutputToLog("Error writing " + this.DeviceName + " configuration to file. " + ex.Message, "red"); // Output info to event log
            }
        }

        // Method for checking HidDevice connection with HostInterface (computer) and reconnecting if applicable
        private bool CheckConnection(bool retry = false)
        {
            bool result = false; // Initialize variable for storing processing result
            try // Attempt the following code...
            {
                if (this.IsConnected == false) // Check if HostInterface (computer) is NOT connected to target HidDevice
                {
                    string connectionFailedMessage = "VoiceAttack is not connected with " + this.DeviceName; // Store output message
                    if (retry == true) // Check if connection with kbDevice should be reattempted
                    {
                        Connect(this.IsListening); // Call method to connect with target HidDevice
                        if (this.IsConnected == false) // Check if HostInterface (computer) is (still) NOT connected to target HidDevice
                            VoiceAttackPlugin.OutputToLog(connectionFailedMessage, "red"); // Output info to event log
                        else
                            result = true; // Update result
                    }
                    else
                        VoiceAttackPlugin.OutputToLog(connectionFailedMessage, "red"); // Output info to event log
                }
                else
                    result = true; // Update result
            }
            catch (Exception ex) // Handle exceptions encountered in above code
            {
                VoiceAttackPlugin.OutputToLog("Error checking connection with " + this.DeviceName + ". " + ex.Message, "red"); // Output info to event log
            }
            /// if (result == true) // Check if result is true (debug)
                /// VoiceAttackPlugin.OutputToLog(this.DeviceName + " is connected", "purple"); // Output info to event log (debug)
            return result;
        }

        // Method for sending data to a HidDevice
        public bool Send(int action, int? context = null, bool retry = false)
        {
            ///throw new Exception("test"); // (debug)
            bool result = false; // Initialize variable for storing processing result
            try // Attempt the following code...
            {
                if (CheckConnection(retry) == false) // Check if HostInterface (computer) is NOT connected with target HidDevice 
                    return result; // Return result from this method

                // Initialize byte array for sending info to HidDevice
                byte[] OutData = new byte[kbDevice.Capabilities.OutputReportByteLength - 1];

                // Enter info for sending to HidDevice
                // This is configured to communicate with QMK (Raw HID)
                OutData[0] = 0; // 'Report ID' not received by QMK, so set to zero
                OutData[1] = (byte)action; // Action for HidDevice to execute
                if (context != null) // Check if context contains data
                    OutData[2] = (byte)context; // Context for desired HidAction

                // Send OutData to HidDevice
                /// VoiceAttackPlugin.OutputToLog("Sending data to " + this.DeviceName, "purple"); // Output info to event log (debug)
                if (kbDevice.Write(OutData) == false) // Send OutData to HidDevice and check if process was NOT successful
                    VoiceAttackPlugin.OutputToLog("Could not send data to " + this.DeviceName, "red"); // Output info to event log
                else
                    result = true; // Update result
            }
            catch (Exception ex) // Handle exceptions encountered in above code
            {
                VoiceAttackPlugin.OutputToLog("Error sending data to " + this.DeviceName + ". " + ex.Message, "red"); // Output info to event log
            }
            return result; // Return result from this method
        }

        // Method for (manually) receiving data from a HidDevice
        public bool Receive(bool retry = false)
        {
            ///throw new Exception("test"); // (debug)
            bool result = false; // Initialize variable for storing processing result
            try // Attempt the following code...
            {
                if (CheckConnection(retry) == false) // Check if HostInterface (computer) is NOT connected with target HidDevice 
                    return result; // Return result from this method

                // Read data received from HidDevice
                HidDeviceData InData = kbDevice.Read(1000); // Read data from HidDevice (with timeout of 1000 ms?)
                if (InData.Status != HidDeviceData.ReadStatus.Success) // Check if reading data from HidDevice was NOT successful
                    VoiceAttackPlugin.OutputToLog("Could not read data from " + this.DeviceName, "red"); // Output info to event log
                else
                {
                    // *Do stuff with data received from HidDevice*

                    // Here is an example for debugging
                    /* if (InData.Data.Length >= 4) // Check if length of received data is greater than or equal to 4 elements (change as needed)
                    {
                        int[] data = Array.ConvertAll(InData.Data, c => (int)c); // Convert received byte data to integer array
                        string[] convertedData = new string[data.Length]; // Initialize string array
                        for (int i = 0; i < data.Length; i++) // Loop through each data element
                            convertedData[i] = Convert.ToChar(data[i]).ToString(); // Convert current data element to equivalent string element
                        VoiceAttackPlugin.OutputToLog(string.Join("", convertedData), "purple"); // Output info to event log (debug)
                    } */

                    result = true; // Update result
                }
            }
            catch (Exception ex) // Handle exceptions encountered in above code
            {
                VoiceAttackPlugin.OutputToLog("Error receiving data from " + this.DeviceName + ". " + ex.Message, "red"); // Output info to event log
            }
            return result; // Return result from this method
        }

        // Method for closing HidDevice interface
        public void Close()
        {
            try // Attempt the following code
            {
                if (kbDevice != null) // Check if kbDevice is still 'active'
                {
                    this.IsActive = false; // Reset flag indicating interface between HostInterface (computer) and HidDevice is NOT active
                    this.IsListening = false; // Disable HostInterface (computer) listening for HidDevice messages
                    kbDevice.Inserted -= DeviceAttachedHandler; // Unsubscribe from HidDevice attachment events
                    kbDevice.Removed -= DeviceRemovedHandler; // Unsubscribe from HidDevice removal events
                    kbDevice.CloseDevice(); // Close connection with kbDevice
                    kbDevice.Dispose(); // Dispose of kbDevice instance
                    kbDevice = null; // Set kbDevice instance as null
                    deviceAttachCount = 0; // Reset counter for device attachment events
                    VoiceAttackPlugin.OutputToLog("Closing interface between VoiceAttack and " + this.DeviceName, "blue"); // Output info to event log
                }
            }
            catch (Exception ex) // Handle exceptions encountered in above code
            {
                VoiceAttackPlugin.OutputToLog("Error closing interface between VoiceAttack and " + this.DeviceName + ". " + ex.Message, "red"); // Output info to event log
            }
        }

        #endregion

        #region Event Methods

        // Method run when HidDevice is attached (with HostInterface having previously identified the HidDevice)
        private void DeviceAttachedHandler()
        {
            deviceAttachCount++;
            if (deviceAttachCount > 1)
                VoiceAttackPlugin.OutputToLog(this.DeviceName + " attached", "blue"); // Output info to event log
        }

        // Method run when HidDevice is removed (with HostInterface having previously identified the HidDevice)
        private void DeviceRemovedHandler()
        {
            VoiceAttackPlugin.OutputToLog(this.DeviceName + " removed", "blue"); // Output info to event log
        }

        // Method run when HidDevice sends data to the (connected and listening) HostInterface (computer)
        private void OnReport(HidReport report)
        {
            if (this.IsConnected == false || this.IsListening == false) // Check if HostInterface (computer) is NOT connected to HidDevice OR is NOT listening for HidDevice messages 
                return; // Return from this method

            // *Do stuff with data received from HidDevice*

            // Here is an example for debugging
            /* if (report.Data.Length >= 4) // Check if length of received data is greater than or equal to 4 elements (change as needed)
            {
                int[] data = Array.ConvertAll(report.Data, c => (int)c); // Convert received byte data to integer array
                string[] convertedData = new string[data.Length]; // Initialize string array
                for (int i = 0; i < data.Length; i++) // Loop through each data element
                    convertedData[i] = Convert.ToChar(data[i]).ToString(); // Convert current data element to equivalent string element
                VoiceAttackPlugin.OutputToLog(string.Join("", convertedData), "purple"); // Output info to event log (debug)
            } */

            kbDevice.ReadReport(OnReport); // Subscribe to OnReport (as callback) for next received message from HidDevice
        }

        #endregion

        #region Processing Methods

        // Method for converting hexadecimal string into integer
        private static int ConvertHexStringToInt(string hexString)
        {
            int intValue;
            if (hexString.StartsWith("0x") == true) // Handle case where hex string is prefixed with "0x" (e.g., 0x61)
                intValue = Convert.ToInt32(hexString, 16);
            else
                intValue = int.Parse(hexString, System.Globalization.NumberStyles.HexNumber);
            return intValue;
        }

        #endregion
    }
}

#region Acknowledgements

// Mike O'Brien and Austin Mullins (and other contributors) for HidLibrary (https://github.com/mikeobrien/HidLibrary)
// Ricardo Amores Hernandez (and other contributors) for ini-parser (https://github.com/rickyah/ini-parser)
// Dasky and fauxpark from the QMK Discord for sharing their code and offering advice during development

#endregion

#region References

// Hex string to int conversion ==> https://theburningmonk.com/2010/02/converting-hex-to-int-in-csharp/
// ASCII to Hex Conversion ==> https://www.rapidtables.com/convert/number/ascii-hex-bin-dec-converter.html
// 'MagtekCardReader' example from HidLibrary v3.3.40 ==> https://github.com/mikeobrien/HidLibrary/releases

#endregion