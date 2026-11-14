namespace TSHR_AutoClock
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(nameof(Pages.CredentialsPage), typeof(Pages.CredentialsPage));
        }
    }
}
