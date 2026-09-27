using System.Text.Json;
using ZombomboUpdater.Core;

namespace ZombomboUpdater;

public sealed class MainForm : Form
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly TextBox repositoryBox = new();
    private readonly TextBox directoryBox = new();
    private readonly Button browseButton = new();
    private readonly Button checkButton = new();
    private readonly ProgressBar progressBar = new();
    private readonly Label statusLabel = new();
    private readonly Label installedLabel = new();
    private readonly Label latestLabel = new();
    private bool busy;

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZombomboUpdater");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public MainForm()
    {
        Text = "Zombombo Güncelleyici";
        ClientSize = new Size(580, 345);
        MinimumSize = new Size(580, 384);
        MaximumSize = new Size(900, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(22, 26, 34);
        ForeColor = Color.WhiteSmoke;
        Padding = new Padding(24);

        var title = new Label { Text = "Zombombo Güncelleyici", Font = new Font("Segoe UI Semibold", 17),
            Location = new Point(24, 18), AutoSize = true };
        var subtitle = new Label { Text = "Açıldığında yeni build'i otomatik indirir ve kurar.",
            ForeColor = Color.FromArgb(165, 175, 190), Location = new Point(26, 58), AutoSize = true };
        var repoLabel = MakeLabel("GitHub deposu (sahip/depo)", 26, 94);
        ConfigureTextBox(repositoryBox, 26, 119, 528);
        var directoryLabel = MakeLabel("Oyun dosyalarının indirileceği klasör", 26, 158);
        ConfigureTextBox(directoryBox, 26, 183, 414);
        directoryBox.ReadOnly = true;
        ConfigureButton(browseButton, "Seç...", 450, 182, 104);
        browseButton.Click += Browse;

        installedLabel = MakeLabel("Kurulu sürüm: —", 26, 228);
        latestLabel = MakeLabel("Son sürüm: —", 290, 228);
        progressBar.Location = new Point(26, 261);
        progressBar.Size = new Size(528, 18);
        progressBar.ForeColor = Color.FromArgb(71, 192, 145);
        statusLabel = MakeLabel("Hazır", 26, 292);
        statusLabel.ForeColor = Color.FromArgb(165, 175, 190);
        ConfigureButton(checkButton, "Şimdi kontrol et", 385, 287, 169);
        checkButton.Click += async (_, _) => await CheckAsync();

        Controls.AddRange([title, subtitle, repoLabel, repositoryBox, directoryLabel, directoryBox,
            browseButton, installedLabel, latestLabel, progressBar, statusLabel, checkButton]);
        LoadSettings();
        Shown += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(repositoryBox.Text) && !string.IsNullOrWhiteSpace(directoryBox.Text))
                await CheckAsync();
        };
        FormClosed += (_, _) => http.Dispose();
    }

    private Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text, Location = new Point(x, y), AutoSize = true
    };

    private static void ConfigureTextBox(TextBox box, int x, int y, int width)
    {
        box.Location = new Point(x, y);
        box.Size = new Size(width, 30);
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = Color.FromArgb(38, 44, 55);
        box.ForeColor = Color.WhiteSmoke;
    }

    private static void ConfigureButton(Button button, string text, int x, int y, int width)
    {
        button.Text = text;
        button.Location = new Point(x, y);
        button.Size = new Size(width, 32);
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.FromArgb(58, 99, 177);
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
    }

    private void Browse(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Oyun dosyalarının kurulacağı klasörü seçin",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(directoryBox.Text) ? directoryBox.Text : ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        directoryBox.Text = dialog.SelectedPath;
        RefreshInstalledVersion();
        SaveSettings();
        if (!string.IsNullOrWhiteSpace(repositoryBox.Text)) _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (busy) return;
        if (string.IsNullOrWhiteSpace(repositoryBox.Text) || string.IsNullOrWhiteSpace(directoryBox.Text))
        {
            statusLabel.Text = "Önce depo ve yükleme klasörü seçin.";
            return;
        }
        busy = true;
        browseButton.Enabled = false;
        checkButton.Enabled = false;
        repositoryBox.Enabled = false;
        try
        {
            SaveSettings();
            RefreshInstalledVersion();
            statusLabel.Text = "Yeni sürüm kontrol ediliyor...";
            progressBar.Value = 0;
            var release = await new ReleaseClient(http).GetLatestAsync(repositoryBox.Text, "game.zip", CancellationToken.None);
            latestLabel.Text = "Son sürüm: " + release.Version;
            var current = GameInstaller.ReadState(directoryBox.Text);
            if (current?.Version == release.Version)
            {
                statusLabel.Text = "Oyun dosyaları güncel.";
                progressBar.Value = 100;
                return;
            }
            var progress = new Progress<UpdateProgress>(p =>
            {
                statusLabel.Text = p.Status;
                if (p.Percent is int percent) progressBar.Value = Math.Clamp(percent, 0, 100);
            });
            await new GameInstaller(http).InstallAsync(release, directoryBox.Text, progress);
            RefreshInstalledVersion();
            statusLabel.Text = "Güncelleme tamamlandı.";
            progressBar.Value = 100;
        }
        catch (ReleaseNotFoundException)
        {
            statusLabel.Text = "Henüz oyun build'i yayınlanmadı.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Güncelleme başarısız.";
            MessageBox.Show(this, ex.Message, "Güncelleme hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            busy = false;
            browseButton.Enabled = true;
            checkButton.Enabled = true;
            repositoryBox.Enabled = true;
        }
    }

    private void RefreshInstalledVersion()
    {
        try { installedLabel.Text = "Kurulu sürüm: " + (GameInstaller.ReadState(directoryBox.Text)?.Version ?? "Yok"); }
        catch { installedLabel.Text = "Kurulu sürüm: Bilinmiyor"; }
    }

    private void LoadSettings()
    {
        try
        {
            repositoryBox.Text = "CTLBs/zombombo-game-builds";
            var defaultsPath = Path.Combine(AppContext.BaseDirectory, "updater-config.json");
            if (File.Exists(defaultsPath))
            {
                var defaults = JsonSerializer.Deserialize<Settings>(File.ReadAllText(defaultsPath));
                repositoryBox.Text = defaults?.Repository ?? "";
            }
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
                if (!string.IsNullOrWhiteSpace(settings?.Repository)) repositoryBox.Text = settings.Repository;
                directoryBox.Text = settings?.InstallDirectory ?? "";
            }
            if (!string.IsNullOrWhiteSpace(directoryBox.Text)) RefreshInstalledVersion();
        }
        catch (Exception ex) { statusLabel.Text = "Ayarlar okunamadı: " + ex.Message; }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(repositoryBox.Text.Trim(), directoryBox.Text)));
    }

    private sealed record Settings(string Repository, string? InstallDirectory = null);
}
