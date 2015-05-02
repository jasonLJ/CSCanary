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
using System.Net.Mail;

namespace CSCanary
{
    class Program
    {
        // Configuration
        private const string CONFIG_PATH = "config.xml";
        private const string LOG_PATH = "log.txt";
        private const int EMAIL_TIMEOUT = 100000;

        // Private Variables
        private static string _internalIP;
        private static string _externalIP;

        private static string _internalURL;
        private static string _externalURL;

        private static int _httpCheckIntervalSeconds;
        private static int _pingCheckIntervalSeconds;

        private static string _smtpHost;
        private static int _smtpPort;
        private static string _smtpUsername;
        private static string _smtpPassword;
        private static string _smtpDestinationAddress;
        private static string _smtpSenderAddress;
        private static bool _smtpUseSSL;

        private static int _emailSendMinimumInterval;
        private static int _lastEmail;

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

            _lastEmail = -1; // Will get set on first email

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
                    // URLs
                    xmlReader.ReadToFollowing("internalURL");
                    _internalURL = xmlReader.ReadElementContentAsString();

                    xmlReader.ReadToFollowing("externalURL");
                    _externalURL = xmlReader.ReadElementContentAsString();

                    // IPs
                    xmlReader.ReadToFollowing("internalIP");
                    _internalIP = xmlReader.ReadElementContentAsString();

                    xmlReader.ReadToFollowing("externalIP");
                    _externalIP = xmlReader.ReadElementContentAsString();

                    // Intervals
                    xmlReader.ReadToFollowing("pingCheckInterval");
                    _pingCheckIntervalSeconds = xmlReader.ReadElementContentAsInt();

                    xmlReader.ReadToFollowing("httpCheckInterval");
                    _httpCheckIntervalSeconds = xmlReader.ReadElementContentAsInt();

                    // SMTP Connection Info
                    xmlReader.ReadToFollowing("smtpHost");
                    _smtpHost = xmlReader.ReadElementContentAsString();

                    xmlReader.ReadToFollowing("smtpPort");
                    _smtpPort = xmlReader.ReadElementContentAsInt();

                    xmlReader.ReadToFollowing("smtpUsername");
                    _smtpUsername = xmlReader.ReadElementContentAsString();

                    xmlReader.ReadToFollowing("smtpPassword");
                    _smtpPassword = xmlReader.ReadElementContentAsString();

                    // SMTP Address Info
                    xmlReader.ReadToFollowing("smtpDestination");
                    _smtpDestinationAddress = xmlReader.ReadElementContentAsString();

                    xmlReader.ReadToFollowing("smtpSender");
                    _smtpSenderAddress = xmlReader.ReadElementContentAsString();

                    // SMTP Miscellaneous Info
                    xmlReader.ReadToFollowing("smtpUseSSL");
                    _smtpUseSSL = xmlReader.ReadElementContentAsBoolean();

                    xmlReader.ReadToFollowing("emailMinimumInterval");
                    _emailSendMinimumInterval = xmlReader.ReadElementContentAsInt();
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

            // Figure out if we need to email this
            int currentTime = Environment.TickCount;
            const int millisPerSecond = 1000;
            const int secondsPerMinute = 60;
            int sendIntervalMillis = _emailSendMinimumInterval * millisPerSecond * secondsPerMinute;

            if (currentTime - _lastEmail > sendIntervalMillis || _lastEmail == -1) // Or there hasn't been an email yet
            {
                SendEMail("CSCanary [HTTP] - Failure", message);
                _lastEmail = Environment.TickCount;
            }
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
            
            // TODO: Remove code redundancy
            // Figure out if we need to email this
            int currentTime = Environment.TickCount;
            const int millisPerSecond = 1000;
            const int secondsPerMinute = 60;
            
            int sendIntervalMillis = _emailSendMinimumInterval * millisPerSecond * secondsPerMinute;
            
            if (currentTime - _lastEmail > sendIntervalMillis || _lastEmail == -1) // Or there hasn't been an email yet
            {
                SendEMail("CSCanary [PING] - Failure", message);
                _lastEmail = Environment.TickCount;
            }
        }

        // Logs a message to the log
        static void LogMessage(string message)
        {
            // Create a writer, append message, close.
            StreamWriter writer = File.AppendText(LOG_PATH);
            writer.WriteLine(message);
            writer.Close();
        }

        // Emails a message
        static void SendEMail(string subject, string body)
        {
            SmtpClient client = new SmtpClient(_smtpHost, _smtpPort);
            client.EnableSsl = _smtpUseSSL;
            client.Timeout = EMAIL_TIMEOUT;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;

            // Authentication
            if(_smtpUsername == "" || _smtpPassword == "") // Incomplete authentication information provided
            {
                // Use default credentials
                client.UseDefaultCredentials = true;
            }
            else
            {
                // Authenticate
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(_smtpUsername, _smtpPassword);
            }

            // Create addresses and the email
            MailAddress sender = new MailAddress(_smtpSenderAddress);
            MailAddress destination = new MailAddress(_smtpDestinationAddress);
            MailMessage email = new MailMessage(sender, destination);

            // Provide email information
            email.Subject = subject;
            email.Body = body;

            client.Send(email);
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
                request.Credentials = CredentialCache.DefaultCredentials;
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
