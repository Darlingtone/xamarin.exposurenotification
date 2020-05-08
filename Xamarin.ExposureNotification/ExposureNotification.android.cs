﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Gms.Nearby.ExposureNotification;

using Nearby = Android.Gms.Nearby.NearbyClass;
using AndroidRiskLevel = Android.Gms.Nearby.ExposureNotification.RiskLevel;
using TemporaryExposureKeyBuilder = Android.Gms.Nearby.ExposureNotification.TemporaryExposureKey.TemporaryExposureKeyBuilder;

[assembly: UsesPermission(Android.Manifest.Permission.Bluetooth)]

namespace Xamarin.ExposureNotifications
{
	public static partial class ExposureNotification
	{
		static IExposureNotificationClient instance;

		static IExposureNotificationClient Instance
			=> instance ??= Nearby.GetExposureNotificationClient(Application.Context);

		static async Task PlatformStart()
		{
			var c = await Handler.GetConfigurationAsync();

			var config = new ExposureConfiguration.ExposureConfigurationBuilder()
				.SetAttenuationScores(c.AttenuationScores)
				.SetDurationScores(c.DurationScores)
				.SetDaysSinceLastExposureScores(c.DaysSinceLastExposureScores)
				.SetTransmissionRiskScores(c.TransmissionRiskScores)
				.SetAttenuationWeight(c.AttenuationWeight)
				.SetDaysSinceLastExposureWeight(c.DaysSinceLastExposureWeight)
				.SetDurationWeight(c.DurationWeight)
				.SetTransmissionRiskWeight(c.TransmissionWeight)
				.SetMinimumRiskScore(c.MinimumRiskScore)
				.Build();

			await Instance.StartAsync(config);
		}

		static Task PlatformStop()
			=> Instance.StopAsync();

		static async Task<bool> PlatformIsEnabled()
			=> await Instance.IsEnabledAsync();

		// Tells the local API when new diagnosis keys have been obtained from the server
		static async Task<(ExposureDetectionSummary, IEnumerable<ExposureInfo>)> PlatformDetectExposuresAsync(IEnumerable<TemporaryExposureKey> diagnosisKeys)
		{
			var batchSize = await Instance.GetMaxDiagnosisKeyCountAsync();

			// Batch up the keys
			var sequence = diagnosisKeys;
			while (sequence.Any())
			{
				var batch = sequence.Take(batchSize);
				sequence = sequence.Skip(batchSize);

				// TODO: RollingDuration is missing

				await Instance.ProvideDiagnosisKeysAsync(
					batch.Select(k => new TemporaryExposureKeyBuilder()
						.SetKeyData(k.KeyData)
						.SetRollingStartIntervalNumber((int)k.RollingStartLong)
						.SetTransmissionRiskLevel(k.TransmissionRiskLevel.ToNative())
						.Build()).ToList());
			}

			var summary = await PlatformGetExposureSummaryAsync();

			IEnumerable<ExposureInfo> info = Array.Empty<ExposureInfo>();
			if (summary?.MatchedKeyCount > 0)
				info = await PlatformGetExposureInformationAsync();

			return (summary, info);
		}

		static async Task<IEnumerable<TemporaryExposureKey>> PlatformGetTemporaryExposureKeys()
		{
			var exposureKeyHistory = await Instance.GetTemporaryExposureKeyHistoryAsync();

			return exposureKeyHistory.Select(k =>
				new TemporaryExposureKey(
					k.GetKeyData(),
					k.RollingStartIntervalNumber,
					TimeSpan.Zero, // TODO: TimeSpan.FromMinutes(k.RollingDuration * 10),
					k.TransmissionRiskLevel.FromNative()));
		}

		internal static async Task<IEnumerable<ExposureInfo>> PlatformGetExposureInformationAsync()
		{
			var exposures = await Instance.GetExposureInformationAsync();
			var info = exposures.Select(d => new ExposureInfo(
				DateTimeOffset.UnixEpoch.AddMilliseconds(d.DateMillisSinceEpoch).UtcDateTime,
				TimeSpan.FromMinutes(d.DurationMinutes),
				d.AttenuationValue,
				d.TotalRiskScore,
				d.TransmissionRiskLevel.FromNative()));
			return info;
		}

		internal static async Task<ExposureDetectionSummary> PlatformGetExposureSummaryAsync()
		{
			var summary = await Instance.GetExposureSummaryAsync();

			// TODO: Reevaluate byte usage here
			return new ExposureDetectionSummary(
				summary.DaysSinceLastExposure,
				(ulong)summary.MatchedKeyCount,
				(byte)summary.MaximumRiskScore);
		}
	}

	static partial class Utils
	{
		public static RiskLevel FromNative(this int riskLevel) =>
			riskLevel switch
			{
				AndroidRiskLevel.RiskLevelLowest => RiskLevel.Lowest,
				AndroidRiskLevel.RiskLevelLow => RiskLevel.Low,
				AndroidRiskLevel.RiskLevelLowMedium => RiskLevel.MediumLow,
				AndroidRiskLevel.RiskLevelMedium => RiskLevel.Medium,
				AndroidRiskLevel.RiskLevelMediumHigh => RiskLevel.MediumHigh,
				AndroidRiskLevel.RiskLevelHigh => RiskLevel.High,
				AndroidRiskLevel.RiskLevelVeryHigh => RiskLevel.VeryHigh,
				AndroidRiskLevel.RiskLevelHighest => RiskLevel.Highest,
				_ => AndroidRiskLevel.RiskLevelInvalid,
			};

		public static int ToNative(this RiskLevel riskLevel) =>
			riskLevel switch
			{
				RiskLevel.Lowest => AndroidRiskLevel.RiskLevelLowest,
				RiskLevel.Low => AndroidRiskLevel.RiskLevelLow,
				RiskLevel.MediumLow => AndroidRiskLevel.RiskLevelLowMedium,
				RiskLevel.Medium => AndroidRiskLevel.RiskLevelMedium,
				RiskLevel.MediumHigh => AndroidRiskLevel.RiskLevelMediumHigh,
				RiskLevel.High => AndroidRiskLevel.RiskLevelHigh,
				RiskLevel.VeryHigh => AndroidRiskLevel.RiskLevelVeryHigh,
				RiskLevel.Highest => AndroidRiskLevel.RiskLevelHighest,
				_ => AndroidRiskLevel.RiskLevelInvalid,
			};
	}
}
