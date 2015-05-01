using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.NetworkInformation;
using System.IO;
using System.Xml;
using System.Timers;

namespace CSCanary
{
    class Program
    {
        // Configuration
        private const string CONFIG_PATH = "config.xml";
        private const string LOG_PATH = "log.txt";

        // Private Variables
        private static string _internalIP;
        private static string _externalIP;

        private static string _internalURL;
        private static string _externalURL;

        private static int _httpCheckIntervalSeconds;
        private static int _pingCheckIntervalSeconds;

        static void Main(string[] args)
        {
            // Load elements from the config file
            LoadConfig();

            // Let the user know what's going on
            Console.WriteLine("Code Sign Canary - Cheep Cheep.");
            Console.WriteLine("Monitoring {0}, {1}, {2}, {3}\nEvery {4} (PING) and {5} (HTTP) seconds.", _internalURL, _externalURL, _internalIP, _externalIP, _pingCheckIntervalSeconds, _httpCheckIntervalSeconds);

            // Convert the seconds we got from the config to millis
            const int millisPerSecond = 1000;
            int pingCheckIntervalMillis = _pingCheckIntervalSeconds * millisPerSecond;
            int httpCheckIntervalMillis = _httpCheckIntervalSeconds * millisPerSecond;

            // Create a timer for ping checking
            Timer pingTimer = new Timer();
            pingTimer.Elapsed += new ElapsedEventHandler(CheckPing);
            pingTimer.Interval = pingCheckIntervalMillis;
            pingTimer.Enabled = true;
            
            // Create a timer for Http checking
            Timer httpTimer = new Timer();
            httpTimer.Elapsed += new ElapsedEventHandler(CheckHttp);
            httpTimer.Interval = httpCheckIntervalMillis;
            httpTimer.Enabled = true;

            // Run the first two checks on startup
            CheckPing(null, null);
            CheckHttp(null, null);

            // Run forever
            while (true) ;
        }

        // Loads settings from the config file
        static void LoadConfig()
        {
            // Attempt to load up the config file
            StreamReader streamReader = null;

            try
            {
                streamReader = new StreamReader(CONFIG_PATH);
            }
            catch (FileNotFoundException) // Could find the file
            {
                // Tell the user we couldn't find the file, then exit
                Console.WriteLine("Could not find config file in local directory. Exiting...");
                Environment.Exit(0);
            }

            // Attempt to create an xmlReader on the file and pull settings
            try
            {
                using (XmlReader xmlReader = XmlReader.Create(streamReader))
                {
                    xmlReader.ReadToFollowing("internalURL");
                    _internalURL = xmlReader.ReadElementContentAsString();

                    xmlReader.ReadToFollowing("externalURL");
                    _externalURL = xmlReader.ReadElementContentAsString();

                    xmlReader.ReadToFollowing("internalIP");
                    _internalIP = xmlReader.ReadElementContentAsString();

                    xmlReader.ReadToFollowing("externalIP");
                    _externalIP = xmlReader.ReadElementContentAsString();

                    xmlReader.ReadToFollowing("pingCheckInterval");
                    _pingCheckIntervalSeconds = xmlReader.ReadElementContentAsInt();

                    xmlReader.ReadToFollowing("httpCheckInterval");
                    _httpCheckIntervalSeconds = xmlReader.ReadElementContentAsInt();
                }
            }
            catch (XmlException) // Some error in the XML
            {
                // Tell the use their config is messed up
                Console.WriteLine("There is an issue with your configuration file. Exiting...");
                Environment.Exit(0);
            }
        }

        // Checks Pings and signals a failure
        static void CheckPing(object source, ElapsedEventArgs e)
        {
            // Update internal ping check
            bool canPingInternal = IsPingable(_internalIP);

            // If we can't connect internally
            if (!canPingInternal)
            {
                // Update external ping check
                bool canPingExternal = IsPingable(_externalIP);

                // Print status
                PrintPingStatus(canPingInternal, canPingExternal);
            }
        }

        // Checks Http connections and signals a failure
        static void CheckHttp(object source, ElapsedEventArgs e)
        {
            // Update internal http check
            bool canHttpInternal = IsContactable(_internalURL);

            // If we can't connect internally
            if (!canHttpInternal)
            {
                // Update external http check
                bool canHttpExternal = IsContactable(_externalURL);

                // Print a status
                PrintHttpStatus(canHttpInternal, canHttpExternal);
            }
        }

        // Print the status of Http check success
        static void PrintHttpStatus(bool canHttpInternal, bool canHttpExternal)
        {
            // Create message
            string time = GetCurrentTime();
            string message = "" + time + " - [HTTP] Internal: " + GetPassedText(canHttpInternal) + " | External: " + GetPassedText(canHttpExternal);

            // Write and log the message
            Console.WriteLine(message);
            LogMessage(message);
        }

        // Prints the status of ping check success
        static void PrintPingStatus(bool canPingInternal, bool canPingExternal)
        {
            // Create message
            string time = GetCurrentTime();
            string message = "" + time + " - [PING] Internal: " + GetPassedText(canPingInternal) + " | External: " + GetPassedText(canPingExternal);

            // Write and log the message
            Console.WriteLine(message);
            LogMessage(message);
        }

        // Logs a message to the log
        static void LogMessage(string message)
        {
            // Create a writer, append message, close.
            StreamWriter writer = File.AppendText(LOG_PATH);
            writer.WriteLine(message);
            writer.Close();
        }

        // Returns a timestamp of right now
        static string GetCurrentTime()
        {
            return DateTime.Now.ToString("yyyy-M-d hh:mm"); 
        }

        // Returns whether or not a website at URL is contactable
        static bool IsContactable(string url)
        {
            // Attempt to create a WebRequest for the URL provided
            HttpWebRequest request = null;
            HttpWebResponse response = null;

            try
            {
                request = (HttpWebRequest) WebRequest.Create(url);
                response = (HttpWebResponse) request.GetResponse();
            }
            catch (WebException)
            {
                // Could not make a connection
                return false;
            }

            // A connection was made
            response.Close();
            return true;
        }

        // Return whether or not a ping to IP was successful
        static bool IsPingable(string ip)
        {
            // Attempt to ping the IP.
            Ping ping = new Ping();
            PingReply reply = ping.Send(ip);

            return reply.Status == IPStatus.Success;
        }

        // Replaces 'true' and 'false' with 'passed' and 'failed'
        static string GetPassedText(bool canConnect)
        {
            return canConnect ? "passed" : "failed";
        }
    }
}
