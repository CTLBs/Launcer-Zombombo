namespace ZombomboUpdater;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\ZombomboUpdater", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Güncelleyici zaten açık.", "Zombombo Güncelleyici",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Application.Run(new MainForm());
    }
}
