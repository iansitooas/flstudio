using System.Windows;
using FLLiteStudio.Core;

namespace FLLiteStudio.UI;

public partial class UpdateWindow : Window
{
    private readonly UpdaterSettings _settings;
    private UpdateReleaseInfo? _foundRelease;
    private CancellationTokenSource? _cts;

    public bool RestartRequired { get; private set; }

    public UpdateWindow(UpdaterSettings settings, UpdateReleaseInfo? preloadedRelease = null)
    {
        InitializeComponent();
        _settings = settings;
        _foundRelease = preloadedRelease;

        LblCurrentVer.Text = $"Versión instalada: v{GitHubUpdater.CurrentVersionString}";
        LblOfficialRepo.Text = $"Origen oficial bloqueado: github.com/{GitHubUpdater.OfficialRepoSlug}";
        ChkAutoCheck.IsChecked = _settings.AutoCheckOnStartup;

        if (_foundRelease != null)
        {
            DisplayRelease(_foundRelease);
        }
        else
        {
            // Comprobar automáticamente al abrir la ventana
            Loaded += (_, _) => BtnCheck_Click(this, new RoutedEventArgs());
        }
    }

    private void SavePrefs()
    {
        _settings.AutoCheckOnStartup = ChkAutoCheck.IsChecked == true;
        GitHubUpdater.SaveSettings(_settings);
    }

    private void DisplayRelease(UpdateReleaseInfo info)
    {
        if (info.IsNewerAvailable)
        {
            string sizeTxt = info.AssetSizeBytes > 0 ? $" ({info.AssetSizeBytes / 1048576.0:0.0} MB)" : "";
            LblStatus.Text = $"Nueva versión disponible: {info.TagName}{sizeTxt}";
            TxtNotes.Text = info.ReleaseNotes;
            BtnInstall.Visibility = info.AssetDownloadUrl != null ? Visibility.Visible : Visibility.Collapsed;
            if (info.AssetDownloadUrl == null)
            {
                TxtNotes.Text += "\n\nNota: Esta publicación todavía no adjunta un archivo .zip o .exe descargable.";
            }
        }
        else
        {
            LblStatus.Text = $"Ya tienes la última versión instalada (v{GitHubUpdater.CurrentVersionString}).";
            TxtNotes.Text = $"Última publicación oficial: {info.TagName}\n\n{info.ReleaseNotes}";
            BtnInstall.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnCheck_Click(object sender, RoutedEventArgs e)
    {
        SavePrefs();
        BtnCheck.IsEnabled = false;
        BtnInstall.Visibility = Visibility.Collapsed;
        LblStatus.Text = "Buscando actualizaciones oficiales...";
        TxtNotes.Text = "";

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            _foundRelease = await GitHubUpdater.CheckForUpdatesAsync(_cts.Token);
            DisplayRelease(_foundRelease);
        }
        catch (Exception ex)
        {
            LblStatus.Text = "No se pudo comprobar actualizaciones.";
            TxtNotes.Text = ex.Message;
        }
        finally
        {
            BtnCheck.IsEnabled = true;
        }
    }

    private async void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_foundRelease == null) return;

        BtnCheck.IsEnabled = false;
        BtnInstall.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        LblStatus.Text = $"Descargando {_foundRelease.TagName}...";

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            PrgDownload.Value = p.Percent;
            LblPercent.Text = $"{p.Percent:0}%";
            double mbRecv = p.BytesReceived / 1048576.0;
            double mbTot = p.TotalBytes / 1048576.0;
            LblProgress.Text = p.TotalBytes > 0
                ? $"Descargando: {mbRecv:0.1} MB de {mbTot:0.1} MB"
                : $"Descargando: {mbRecv:0.1} MB";
        });

        try
        {
            bool shouldExit = await GitHubUpdater.DownloadAndApplyUpdateAsync(_foundRelease, progress, _cts.Token);
            if (shouldExit)
            {
                LblStatus.Text = "Instalando actualización y reiniciando...";
                RestartRequired = true;
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            LblStatus.Text = "Error al descargar o instalar la actualización.";
            TxtNotes.Text = ex.Message;
            BtnCheck.IsEnabled = true;
            BtnInstall.IsEnabled = true;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        SavePrefs();
        Close();
    }
}
