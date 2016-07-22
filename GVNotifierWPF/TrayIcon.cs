using DavuxLib2;
using System.Threading;
using System.Windows.Forms;

namespace GVNotifier
{
    public class TrayIcon
    {
        public static void Init()
        {
            if (Settings.Get("ShowTrayIcon", false))
            {
                var t = new Thread(() =>
                {
                    NotifyIcon ni = new NotifyIcon
                    {
                        Icon = Properties.Resources.gv,
                        Text = "GVNotifier",
                        Visible = true,
                        ContextMenu = new ContextMenu()
                    };

                    var m = new MenuItem("About");
                    m.Click += (ss, ee) => SessionModel.ShowAbout();
                    ni.ContextMenu.MenuItems.Add(m);

                    m = new MenuItem("Preferences");
                    m.Click += (ss, ee) => SessionModel.ShowPrefs();
                    ni.ContextMenu.MenuItems.Add(m);

                    ni.ContextMenu.MenuItems.Add(new MenuItem("-"));

                    m = new MenuItem("Check for new messages");
                    m.Click += (ss, ee) => SessionModel.Check();
                    ni.ContextMenu.MenuItems.Add(m);

                    // TODO contact name sync won't work, and number sync
                    // won't work if the window has been opened in the session
                    // this comes together with the "switch to MVVM" change.
                    m = new MenuItem();
                    m.Text = "Sync Google Contacts Now";
                    m.Click += (ss, ee) => SessionModel.UpdateContacts();
                    ni.ContextMenu.MenuItems.Add(m);

                    ni.ContextMenu.MenuItems.Add(new MenuItem("-"));

                    m = new MenuItem("Sign Out");
                    m.Click += (ss, ee) =>
                    {
                        ni.Visible = false;
                        SessionModel.SignOut();
                    };
                    ni.ContextMenu.MenuItems.Add(m);

                    m = new MenuItem("Quit");
                    m.Click += (ss, ee) =>
                    {
                        ni.Visible = false;
                        SessionModel.CloseApp();
                    };
                    ni.ContextMenu.MenuItems.Add(m);

                    ni.DoubleClick += (_, __) => SessionModel.ShowMainWindow();
                    ni.MouseClick += (ss, ee) =>
                    {
                        if (ee.Button == MouseButtons.Left)
                        {
                            SessionModel.ShowMainWindow();
                        }
                    };

                    Application.Run();
                });
                // this worked without STA, but UI controls should always be STA
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
        }
    }
}
