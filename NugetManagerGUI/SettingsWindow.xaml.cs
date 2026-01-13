using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace NugetManagerGUI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        var cfg = Settings.Load();
        FeedUrlText.Text = cfg.FeedUrl ?? string.Empty;
        ApiKeyText.Text = cfg.ApiKey ?? string.Empty;

        SaveButton.Click += (s, e) =>
        {
            var newCfg = new Settings { FeedUrl = FeedUrlText.Text.Trim(), ApiKey = ApiKeyText.Text.Trim() };
            try
            {
                newCfg.Save();
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存设置时出错：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        CancelButton.Click += (s, e) => { this.DialogResult = false; this.Close(); };
    }
}
