using Kindergarten3_TR_Installer.Resources;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Kindergarten3_TR_Installer
{
    public partial class MainWindow : Window
    {
        private string gameFolderPath = "";
        private const string GameExeName = "Kindergarten3.exe";
        private string gameVersion = "";

        public MainWindow()
        {
            InitializeComponent();
            ResetUI();
        }

        private void ResetUI()
        {
            InstallButton.IsEnabled = false;
            UninstallButton.IsEnabled = false;
            ReportButton.Visibility = Visibility.Collapsed;
            StatusText.Text = "Durum: Oyun klasörü seçiniz.";
        }

        private async void SelectGameFolder_Click(object sender, RoutedEventArgs e)
        {
            string initialFolder = GetInitialFolder();

            var dlg = new OpenFileDialog
            {
                Filter = $"{GameExeName}|{GameExeName}",
                Title = "Oyun Klasörünü Seçin",
                InitialDirectory = initialFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dlg.ShowDialog() != true) return;

            gameFolderPath = Path.GetDirectoryName(dlg.FileName);
            string exePath = Path.Combine(gameFolderPath, GameExeName);
			string assemblyPath = Path.Combine(gameFolderPath, "Kindergarten3_Data/Managed/Assembly-CSharp.dll");

            if (!File.Exists(exePath))
            {
                ShowError($"{GameExeName} bulunamadı.");
                return;
            }
			
			if (!File.Exists(assemblyPath))
            {
                ShowError("Oyun dosyaları doğrulanamadı.");
                return;
            }

            StatusText.Text = "Durum: Versiyon bilgisi indiriliyor...";
            var checker = new GameVersionChecker();

            if (!await checker.UpdateKnownHashesFromGitHubAsync("https://raw.githubusercontent.com/SkipperSkipTR/Kindergarten3-TR/refs/heads/main/knownHashes.json"))
            {
                ShowError("Versiyon bilgisi alınamadı. İnternet bağlantınızı kontrol edin.");
                return;
            }

            StatusText.Text = "Durum: Oyun versiyonu kontrol ediliyor...";
            string fileHash = await ComputeFileHashAsync(assemblyPath);

            if (checker.IsKnownHash(fileHash))
            {
                gameVersion = checker.GetVersionByHash(fileHash);
                SetUIForKnownVersion(gameVersion);
            }
            else
            {
                SetUIForUnknownVersion(fileHash);
            }
        }

        private string GetInitialFolder()
        {
            string steamPath = SteamHelper.GetSteamPathFromRegistry();
            if (string.IsNullOrEmpty(steamPath)) return null;

            var steamLibs = SteamHelper.GetSteamLibraryFolders(steamPath);
            return SteamHelper.FindGameFolderInSteamLibraries(steamLibs, GameExeName);
        }

        private void SetUIForKnownVersion(string version)
        {
            StatusText.Text = $"Durum: Oyun versiyonu doğrulandı. Versiyon: {version}";
            CheckModStatus(version);
            ReportButton.Visibility = Visibility.Collapsed;
        }

        private void SetUIForUnknownVersion(string hash)
        {
            StatusText.Text = $"Durum: Bilinmeyen oyun versiyonu. SHA256: {hash}";
            InstallButton.IsEnabled = false;
            UninstallButton.IsEnabled = false;
            ReportButton.Visibility = Visibility.Visible;

            MessageBox.Show(
                "Oyun dosyalarınız veritabanındaki bilgiler ile uyuşmuyor. " +
                "Bu durum oyun güncellemesi veya yanlış klasör seçilmesi kaynaklı yaşanabilir." +
                "Doğru klasörü seçtiğinizden emin olun veya hata raporu gönderin.",
                "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowError(string message)
        {
            MessageBox.Show(message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Durum: " + message;
        }

        private Task<string> ComputeFileHashAsync(string filePath)
        {
            return Task.Run(() =>
            {
                var sha256 = SHA256.Create();
                var stream = File.OpenRead(filePath);

                var hashBytes = sha256.ComputeHash(stream);
                var sb = new StringBuilder(hashBytes.Length * 2);
                foreach (byte b in hashBytes)
                    sb.AppendFormat("{0:x2}", b);

                return sb.ToString();
            });
        }

        private void BackupFiles(string archivePath, string gameFolder, string backupFolder)
        {
            if (!Directory.Exists(backupFolder))
                Directory.CreateDirectory(backupFolder);

            using (var archive = new Ionic.Zip.ZipFile(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    string relativePath = entry.FileName;
                    string sourceFile = Path.Combine(gameFolder, relativePath);
                    string backupFile = Path.Combine(backupFolder, relativePath);

                    if (File.Exists(sourceFile))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(backupFile));
                        File.Copy(sourceFile, backupFile, true);
                    }
                }
            }
        }
        private void CheckModStatus(string version)
        {
            string backupPath = System.IO.Path.Combine(gameFolderPath, "Yedek");

            if (Directory.Exists(backupPath))
            {
                StatusText.Text = $"Durum: Yama yüklü. Versiyon: {version}";
                InstallButton.IsEnabled = false;
                UninstallButton.IsEnabled = true;
            }
            else
            {
                StatusText.Text = $"Durum: Yama yüklü değil. Versiyon: {version}";
                InstallButton.IsEnabled = true;
                UninstallButton.IsEnabled = false;
            }

            ReportButton.Visibility = Visibility.Collapsed; // Hide report button if version is valid
        }


        private async Task DownloadFileWithProgressAsync(string url, string destinationPath)
        {
            using (var httpClient = new HttpClient())
            using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;

                        if (totalBytes.HasValue)
                        {
                            double progress = (double)totalRead / totalBytes.Value * 100;
                            Dispatcher.Invoke(() =>
                            {
                                ProgressBar.Value = progress;
                                StatusText.Text = $"Durum: İndirme %{(int)progress}";
                            });
                        }
                    }
                }
            }
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                InstallButton.IsEnabled = false;
                ProgressBar.Value = 0;
                StatusText.Text = "Durum: Yama indiriliyor...";

                string version = gameVersion;
                string archiveFileName = version + ".zip";
                string archiveUrl = $"https://github.com/SkipperSkipTR/Kindergarten3-TR/releases/download/{version}/{archiveFileName}";
                string archivePath = Path.Combine(Path.GetTempPath(), version + ".zip");
                string destinationPath = gameFolderPath;
                string backupFolder = Path.Combine(destinationPath, "Yedek");

                // Download zip with progress
                await DownloadFileWithProgressAsync(archiveUrl, archivePath);

                // 1. Backup existing files
                BackupFiles(archivePath, destinationPath, backupFolder);

                // Extract archive
                StatusText.Text = "Durum: Arşivden çıkarılıyor...";
                string sevenZipPath = ZipHelper.Extract7ZipIfMissing(gameFolderPath);

                // 2. Extract zip
                var extractProcess = new ProcessStartInfo
                {
                    FileName = sevenZipPath,
                    Arguments = $"x \"{archivePath}\" -o\"{destinationPath}\" -y -bsp1",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = extractProcess })
                {
                    process.Start();
                    process.WaitForExit();
                }

                // 3. Delete temp files
                if (File.Exists(sevenZipPath))
                    File.Delete(sevenZipPath);

                if (File.Exists(archivePath))
                    File.Delete(archivePath);

                // 4. Enable uninstall
                InstallButton.IsEnabled = false;
                UninstallButton.IsEnabled = true;
                StatusText.Text = "Durum: İşlem tamamlandı.";
                MessageBox.Show("Yükleme başarılı!", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Yükleme hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string backupFolder = Path.Combine(gameFolderPath, "Yedek");

                if (!Directory.Exists(backupFolder))
                {
                    MessageBox.Show("Yedek bulunamadı. Yama silme mümkün değil.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                foreach (var file in Directory.GetFiles(backupFolder, "*", SearchOption.AllDirectories))
                {
                    string relativePath = file.Substring(backupFolder.Length + 1);
                    string destinationFile = Path.Combine(gameFolderPath, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));
                    File.Copy(file, destinationFile, true);
                }

                Directory.Delete(backupFolder, true);
				
				// Clean up BepInEx
				Directory.Delete(Path.Combine(gameFolderPath, "BepInEx"), true);
				if(Directory.Exists(Path.Combine(gameFolderPath, "assets")))
				{
					Directory.Delete(Path.Combine(gameFolderPath, "assets"), true);
				}
				
				File.Delete(Path.Combine(gameFolderPath, ".doorstop_version"));
				File.Delete(Path.Combine(gameFolderPath, "changelog.txt"));
				File.Delete(Path.Combine(gameFolderPath, "doorstop_config.ini"));
				File.Delete(Path.Combine(gameFolderPath, "winhttp.dll"));

                UninstallButton.IsEnabled = false;
                InstallButton.IsEnabled = true;
                MessageBox.Show("Yama başarıyla kaldırıldı!", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kaldırma hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async void ReportButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(gameFolderPath)) return;

            string exePath = Path.Combine(gameFolderPath, GameExeName);
            string assemblyPath = Path.Combine(gameFolderPath, "Kindergarten3_Data/Managed/Assembly-CSharp.dll");
            if (!File.Exists(exePath))
            {
                ShowError("Oyunun .exe dosyası bulunamadı.");
                return;
            }

            string hash = await ComputeFileHashAsync(assemblyPath);
            bool success = await ReportSender.SendReportAsync("Kindergarten 3", hash, exePath);

            if (success)
            {
                MessageBox.Show("Versiyon raporu gönderildi. Teşekkürler! :white_check_mark:", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                ReportButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowError("Rapor gönderilemedi. İnternet bağlantınızı kontrol edin.");
            }
        }
    }
}
