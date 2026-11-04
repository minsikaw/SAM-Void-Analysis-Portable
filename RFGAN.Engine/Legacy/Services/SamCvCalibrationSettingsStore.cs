using System;
using System.IO;
using System.Text.Json;

namespace RFGAN.SampleMapper.Services;

public static class SamCvCalibrationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsDirectory
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
                appData = AppContext.BaseDirectory;
            return Path.Combine(appData, "RFGAN.SampleMapper", "CvCalibration");
        }
    }

    public static string SettingsPath => Path.Combine(SettingsDirectory, "sam_sample_tsv_detector_advanced_config.json");

    public static SamAdvancedDetectorConfig Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new SamAdvancedDetectorConfig();

            SamAdvancedDetectorConfig? cfg = JsonSerializer.Deserialize<SamAdvancedDetectorConfig>(File.ReadAllText(SettingsPath), JsonOptions);
            return cfg ?? new SamAdvancedDetectorConfig();
        }
        catch (Exception ex)
        {
            AppLogger.Warning("SamCvCalibrationSettingsStore", "Could not load advanced CV settings from {Path}. Error={Error}", SettingsPath, ex.Message);
            return new SamAdvancedDetectorConfig();
        }
    }

    public static void Save(SamAdvancedDetectorConfig config, string updatedBy)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            config.UpdatedAt = DateTime.Now;
            config.UpdatedBy = updatedBy;
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(config, JsonOptions));
            AppLogger.Info("SamCvCalibrationSettingsStore", "Saved advanced CV settings. Path={Path}, UpdatedBy={UpdatedBy}, Params={Params}", SettingsPath, updatedBy, config.Params.ToSummary());
        }
        catch (Exception ex)
        {
            AppLogger.Error("SamCvCalibrationSettingsStore", ex, "Could not save advanced CV settings to {Path}", SettingsPath);
        }
    }

    public static void ResetToDefaults(string updatedBy)
        => Save(new SamAdvancedDetectorConfig(), updatedBy);
}
