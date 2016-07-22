using DavuxLib2;
using GoogleVoice;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace GVNotifier
{
    public interface IMessage
    {
        DateTime Time { get; }
        string Name { get; }
        string ImageLocation { get; }
        string Text { get; }
        Message.MessageType MessageType { get; }
        Contact Contact { get; }
        Message Message { get; }
        bool Search();
    }
    public class BaseMessage : IMessage, INotifyPropertyChanged
    {
        public string Name => Contact.Name;
        public string ImageLocation => Contact.ImageLocation;
        public DateTime Time => msg.Time;
        public Message.MessageType MessageType => msg.Class;
        public string Text => msg.MessageText;
        public Message Message => msg;
        public Contact Contact { get; protected set; }
        Message msg = null;

        public BaseMessage(Contact contact, Message message)
        {
            Contact = contact;
            msg = message;

            Contact.PropertyChanged += (oo, ee) =>
            {
                if (ee.PropertyName == "ImageLocation" || ee.PropertyName == "Name")
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(ee.PropertyName));
            };
        }

        public virtual bool Search() { return false; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
    public class VoiceMessage : BaseMessage
    {
        public VoiceMessage(Contact contact, Message message) : base(contact, message) { }
        public override string ToString() => $"voicemail from {Contact.Name} at {Time}";
    }

    public class CallMessage : BaseMessage
    {
        public CallMessage(Contact contact, Message message) : base(contact, message) { }
        public override string ToString() => $"{MessageType} call from {Name} at {Time}";
    }

    class SessionModel
    {
        public static event Action AppClosing;
        public static event Action ShowRequest;
        public static event Action PrefsRequest;
        //public static event Action CheckRequest;
        public static event Action AboutRequest;

        internal static void ShowAbout() => AboutRequest?.Invoke();
        internal static void ShowPrefs() => PrefsRequest?.Invoke();

        internal static void Check()
        {
            Trace.WriteLine("Manual Message Check");
            Inst?.account?.UpdateAsync();
        }

        internal static void ShowMainWindow() => ShowRequest?.Invoke();
        internal static void CloseApp() => AppClosing?.Invoke();
        internal static void UpdateContacts() => Inst?.account?.UpdateContactsAync();

        internal static void SignOut()
        {
            Trace.WriteLine("Sign Out");
            Settings.Set("AutoLogin", false);
            AppClosing?.Invoke();
            Process.Start(System.Windows.Forms.Application.ExecutablePath, "--no-mutex");
        }

        [STAThread()]
        public static void Main(string[] args)
        {
            bool auto_login_disabled = false;
            try
            {
                System.Windows.Forms.Application.EnableVisualStyles();
            }
            catch { }

            if (args.Length > 0 && args[0].StartsWith("/"))
            {
                try
                {
                    using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", "gvnotifier_wpf", PipeDirection.Out))
                    {

                        // Connect to the pipe or wait until the pipe is available.
                        Console.Write("Attempting to connect to pipe...");
                        pipeClient.Connect(1000);
                        using (StreamWriter sw = new StreamWriter(pipeClient))
                        {
                            sw.WriteLine(args[0]);
                            sw.Flush();
                            sw.Close();
                            Thread.Sleep(2000);
                        }
                        pipeClient.Close();
                    }
                    return;
                }
                catch (TimeoutException)
                {
                    // launch app.
                    auto_login_disabled = true;
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.ToString());
                    return;
                }
            }

            try
            {
                DavuxLib2.App.Init("GVNotifierWPF");

                if (!args.Contains("--no-mutex"))
                {
                    if (DavuxLib2.App.IsAppAlreadyRunning())
                    {
                        try
                        {
                            using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", "gvnotifier_wpf", PipeDirection.Out))
                            {

                                // Connect to the pipe or wait until the pipe is available.
                                Console.Write("Attempting to connect to pipe...");
                                pipeClient.Connect(1000);
                                using (StreamWriter sw = new StreamWriter(pipeClient))
                                {
                                    sw.WriteLine("/show");
                                    sw.Flush();
                                    sw.Close();
                                    Thread.Sleep(2000);
                                }
                                pipeClient.Close();
                            }
                            return;
                        }
                        catch (TimeoutException)
                        {
                            // launch app.
                            auto_login_disabled = true;
                        }
                        catch (Exception ex)
                        {
                            System.Windows.Forms.MessageBox.Show(ex.ToString());
                            return;
                        }
                    }
                }

                DefaultSettings();

                if (auto_login_disabled)
                {
                    Settings.Set("AutoLogin", false);
                }

                new Thread(() =>
                {
                    Trace.WriteLine("Starting Pipe Server");
                    int t = 0;
                    while (true)
                    {
                        try
                        {
                            using (NamedPipeServerStream pipeServer = new NamedPipeServerStream("gvnotifier_wpf", PipeDirection.In))
                            {
                                Trace.WriteLine("Waiting for pipe connection...");
                                pipeServer.WaitForConnection();

                                Trace.WriteLine("Client connected.");
                                try
                                {
                                    // Read user input and send that to the client process.
                                    using (StreamReader sr = new StreamReader(pipeServer))
                                    {
                                        string line = sr.ReadLine();
                                        Trace.WriteLine("PIPE: " + line);
                                        JumpListCommand(line);
                                        sr.Close();
                                    }
                                }
                                // Catch the IOException that is raised if the pipe is broken
                                // or disconnected.
                                catch (IOException e)
                                {
                                    Trace.WriteLine("Pipe ERROR: {0}", e.Message);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine("*** Pipe Server Crashed: " + ex.Message);
                            t++;
                            if (t > 1)
                            {
                                Trace.WriteLine("Pipe Server Crashed Max Times");
                                return;
                            }
                        }
                    }
                }).Start();

                TrayIcon.Init();

                try
                {
                    if (DavuxLib2.App.IsAllowedToExecute(LicensingMode.Free) != LicenseValidity.OK)
                    {
                        Trace.WriteLine("Info: Software could not be properly licensed.");
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("IsAllowedToExecute: " + ex);
                }

                DavuxLib2.App.SubmitCrashReports();

                App app = new App();
                app.InitializeComponent();
                app.Run();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
            }
        }

        private static void DefaultSettings()
        {
            Settings.Get("ViewFontSize", 35); // 35px

            Settings.Get("HideFocusLost", false);
            Settings.Get("HideStartup", false); // NOT ENABLED
            Settings.Get("HideAfterSelect", false);

            // People on Windows 7/8+ won't get the tray icon by default.
            Settings.Get("ShowTrayIcon", IsLegacyWindows());

            Settings.Get("FlashWindow", true);
            Settings.Get("PlaySound", true);
            Settings.Get("SoundRepeat", false);
                     
            Settings.Get("SoundRepeatDuration", 2);
            Settings.Get("SoundRepeeatNumber", 5);

            Settings.Get("ShowAcceptedCall", false);
            Settings.Get("ShowMissedCall", true);
            Settings.Get("ShowPlacedCall", false);

            try
            {
                // We ship the chime sound with the software, but it gets copied into
                // the AppData directory so the user can change it if they like.
                var file = DavuxLib2.App.DataDirectory + "\\new.wav";
                if (!File.Exists(file)) File.Copy("new.wav", file);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Couldn't copy new.wav: " + ex.Message);
            }
        }

        // Legacy Windows is Windows < 6.1 (Windows 7)
        private static bool IsLegacyWindows() => Environment.OSVersion.Version.Major < 6 
            || (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor == 0);

        private static void JumpListCommand(string line)
        {
            try
            {
                if (Inst != null && line.StartsWith("/jump:"))
                {
                    line = line.Substring("/jump:".Length);
                    int code = int.Parse(line);

                    Inst.OnJumpListContact?.Invoke(Inst.Contacts.First(
                        c => c.ID.GetHashCode() == code));
                    return;
                }
                line = line.Trim();
                if (line.Trim() == "/prefs")
                {
                    Trace.WriteLine("Open Prefs");
                    PrefsRequest?.Invoke();
                }
                else if (line == "/gv")
                {
                    Trace.WriteLine("Open Google Voice");
                    Process.Start("http://voice.google.com/");
                }
                else if (line == "/signout")
                {
                    SignOut();
                }
                else if (line == "/check")
                {
                    Trace.WriteLine("Manual Message Check");
                    Inst?.account?.UpdateAsync();
                }
                else if (line == "/quit")
                {
                    Trace.WriteLine("Quit App");
                    AppClosing?.Invoke();
                }
                else if (line == "/show")
                {
                    Trace.WriteLine("Show App");
                    ShowRequest?.Invoke();
                }
                else if (line == "/about")
                {
                    Trace.WriteLine("Show About");
                    ShowAbout();
                }
                else if (line == "/update_contacts")
                {
                    Trace.WriteLine("Update Contacts");
                    UpdateContacts();
                }
                else
                {
                    Debug.Assert(false);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("open contact failed : " + ex);
            }
        }

        string UserName = "";
        string Password = "";

        public Account account = null;

        public static SessionModel Inst = null;

        public delegate void ContactEvent(Contact contact);
        public event ContactEvent OnJumpListContact;

        public ObservableCollection<Contact> Contacts => account.ContactsManager.Contacts ?? new ObservableCollection<Contact>();
        public ObservableCollectionEx<Contact> SearchContacts { get; private set; }
        public ObservableCollectionEx<VoiceMessage> SearchVoicemail { get; private set; }
        public ObservableCollectionEx<VoiceMessage> Voicemails { get; private set; }
        public ObservableCollectionEx<CallMessage> Calls { get; private set; }
        public ObservableCollectionEx<CallMessage> SearchCalls { get; private set; }

        public ObservableCollectionEx<IMessage> All
        {
            get
            {
                var c = new ObservableCollectionEx<IMessage>();
                c.AddRange(Calls.ToArray());
                c.AddRange(Voicemails.ToArray());
                c.Sort(new MessageComparer<IMessage>());
                return c;
            }
        }

        public event Action<Message,Contact> OnMessage;
        public event Action OnContactsUpdated;
        public event Action<string> OnLoginMessage;
        public event Action OnLoginSuccess;
        public event Action OnLoginFailure;
        public static string KEY = "ENC_DAVE_GVN_WPF_%%345!@#%#!$";

        private bool Search(Contact c, string text) => c.Name.ToLower().Contains(text) || c.Phones.Exists(p => p.Number.Contains(text));
        public void Search_Contacts(string text)
        {
            ThreadPool.QueueUserWorkItem((o) =>
            {
                try
                {
                    text = text.ToLower();
                    SearchContacts.Clear();
                    SearchContacts.AddRange(Contacts.Where(c => Search(c, text)));
                    text = Util.StripNumber(text);
                    if (Regex.IsMatch(text, "^[0-9|\\+]+$"))
                        SearchContacts.Add(new Contact
                        {
                            Name = Util.FormatNumber(text),
                            Group = "Dial",
                            Phones = new List<Phone> { new Phone{ Number = text, Type = "Unknown" } }
                        });
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("Contacts search: " + ex);
                }
            });
        }

        public void Search_Calls(string text)
        {
            ThreadPool.QueueUserWorkItem((o) =>
            {
                try
                {
                    text = text.ToLower();
                    SearchCalls.Clear();
                    SearchCalls.AddRange(Calls.Where(c => Search(c.Contact, text)));
                    SearchCalls.Sort(new MessageComparer<CallMessage>());
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("Call Search Error: " + ex);
                }
            });
        }

        public void Search_Voicemail(string text)
        {
            ThreadPool.QueueUserWorkItem((o) =>
            {
                try
                {
                    text = text.ToLower();
                    SearchVoicemail.Clear();
                    SearchVoicemail.AddRange(Voicemails.Where(c => Search(c.Contact, text) || c.Text.ToLower().Contains(text)));
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("VM Search Error: " + ex);
                }
            });
        }

        public SessionModel(string UserName, string Password)
        {
            this.UserName = UserName;
            this.Password = Password;
            Inst = this;
            SearchContacts = new ObservableCollectionEx<Contact>();
            Voicemails = new ObservableCollectionEx<VoiceMessage>();
            SearchVoicemail = new ObservableCollectionEx<VoiceMessage>();
            Calls = new ObservableCollectionEx<CallMessage>();
            SearchCalls = new ObservableCollectionEx<CallMessage>();
            var cachedir = DavuxLib2.App.DataDirectory + @"\cache\";
            try
            {
                if (!Directory.Exists(cachedir)) Directory.CreateDirectory(cachedir);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("SessionModel/SessionModel Create Cache Dir *** " + ex);
            }
            account = new Account(UserName, Password, Settings.Get<GVCookie>("SMSV", null), cachedir);

            account.OnMessage += new Action<Message>(account_OnMessage);
            // account.OnPreInitMessage += new Action<Message>(account_OnPreInitMessage);
            account.OnLoginMessage += (s) => OnLoginMessage?.Invoke(s);
            account.ContactsManager.OnContactsUpdated += () =>
            {
                Trace.WriteLine("Saving Contacts");
                account.ContactsManager.Save();
            };
            account.SMSVCookieUpdated += cookie =>
            {
                Trace.WriteLine("SMSV Cookie Updated: " + cookie);
                Settings.Set("SMSV", cookie);
                Settings.Save();
            };
            account.GetSMSPinFromUser += () =>
            {
                Trace.WriteLine("Requesting PIN from user...");
                // this user has 2-step verification, and we need to get a PIN from them
                string PIN = "";
                var t = new Thread(() =>
                {
                    GetPin pinWindow = new GetPin();
                    pinWindow.Show();
                    System.Windows.Threading.Dispatcher.Run();
                    PIN = pinWindow.PIN;
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
                Trace.WriteLine("Got PIN: " + PIN);
                return PIN;
            };
        }

        void account_OnPreInitMessage(Message m)
        {
            if (m.Class == Message.MessageType.SMS)
            {
                // drop SMS's
                return;
            }
            OnMessage?.Invoke(m, account.ContactForNumber(m.Number));
        }

        public void Save()
        {
            account?.ContactsManager.Save();
            Settings.Save();
        }

        void account_OnMessage(Message m) => OnMessage?.Invoke(m, account.ContactForNumber(m.Number));

        public void Login()
        {
            // Change the polling to 10s from 20s in 1.4.3.180
            if (!Settings.Get("NewPolling_1.4.3", false))
            {
                Settings.Set("UpdateFreq", 10);
                Settings.Set("NewPolling_1.4.3", true);
            }

            new Thread(() =>
                {
                    retry:
                    try
                    {
                        account.Login();
                        try
                        {
                            account.ContactsManager.OnContactsUpdated += () => OnContactsUpdated?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine("Error in contacts assoc " + ex);
                        }

                        account.Ready += () =>
                        {
                            foreach (var m in account.VoiceMailFeed.Messages.ToArray().Reverse())
                            {
                                if (Voicemails.Any(vm => vm.Message == m)) continue;
                                Voicemails.Add(new VoiceMessage(account.ContactForNumber(m.Number), m));
                            }
                            foreach (var m in account.PlacedCalls.Messages)
                            {
                                if (Calls.Any(cm => cm.Message == m)) continue;
                                Calls.Add(new CallMessage(account.ContactForNumber(m.Number), m));
                            }
                            foreach (var m in account.MissedCalls.Messages)
                            {
                                if (!Calls.Any(cm => cm.Message == m))
                                    Calls.Add(new CallMessage(account.ContactForNumber(m.Number), m));
                            }
                            foreach (var m in account.ReceivedCalls.Messages)
                            {
                                if (Calls.Any(cm => cm.Message == m)) continue;
                                Calls.Add(new CallMessage(account.ContactForNumber(m.Number), m));
                            }
                            Calls.Sort(new MessageComparer<CallMessage>());
                            Voicemails.Sort(new MessageComparer<VoiceMessage>());
                        };

                        account.OnMessage += (m) =>
                        {
                            switch (m.Class)
                            {
                                case Message.MessageType.Voicemail:
                                    Trace.Write("Adding vm: " + m);
                                    Voicemails.Insert(0, new VoiceMessage(account.ContactForNumber(m.Number), (VoiceMailMessage)m));
                                    break;
                                case Message.MessageType.Missed:
                                case Message.MessageType.Placed:
                                case Message.MessageType.Received:
                                    Trace.Write("Adding call: " + m);
                                    Calls.Add(new CallMessage(account.ContactForNumber(m.Number), m));
                                    Calls.Sort(new MessageComparer<CallMessage>());
                                    break;
                            }
                        };

                        new Thread(() =>
                        {
                            for (;;)
                            {
                                int updateFreq = Settings.Get("UpdateFreq", 10);
                                if (updateFreq == -1) return;
                                Thread.Sleep(1000 * Math.Max(updateFreq, 5));
                                account.UpdateAsync();
                            }
                        }).Start();

                        OnLoginSuccess?.Invoke();
                    }
                    catch (GVLoginException ex)
                    {
                        Trace.WriteLine("Login Failed: " + ex);
                        // bad password
                        OnLoginFailure?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("Login Failed: " + ex);
                        OnLoginMessage?.Invoke("No Internet Connection, retrying in 10 seconds.");
                        Thread.Sleep(10000);
                        goto retry;
                    }
                }).Start();
        }

        class MessageComparer<T> : IComparer<T> where T:IMessage
        {
            public int Compare(T c1, T c2) => c1 != null && c2 != null ? c2.Time.CompareTo(c1.Time) : 1;
        }
    }
}
