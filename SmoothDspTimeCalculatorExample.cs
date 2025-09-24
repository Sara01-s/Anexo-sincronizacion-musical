using static Unity.Mathematics.math;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Synchronizes Unity's real time with AudioSettings.dspTime using linear regression.
///
/// Problem: AudioSettings.dspTime can have jitter and inconsistencies that affect rhythm games.
/// Solution: Collect samples of both real time and DSP time to calculate `Smooth DSP Time` using linear regression.
///
/// Usage:
/// - Call UpdateLinearRegression() every frame.
/// - Use SmoothDspTime instead of AudioSettings.dspTime for precise audio timing.
/// - Check IsReady to know if regression is active.
/// </summary>
public class SmoothDspTimeCalculatorExample {
	/// <summary>
	/// Returns a smooth, monotonic DSP time calculated from linear regression.
	/// This time is guaranteed to never go backwards and has reduced jitter.
	/// </summary>
	public double SmoothDspTime {
		get {
			// Predict DSP time based on current real time using our linear model: y = beta_0 * x + beta_1.
			double currentSmoothDspTime = _slope * Time.unscaledTimeAsDouble + _intercept;

			// Ensure time never goes backwards (monotonic guarantee).
			currentSmoothDspTime = max(currentSmoothDspTime, _previousSmoothDspTime);

			_previousSmoothDspTime = currentSmoothDspTime;

			return currentSmoothDspTime;
		}
	}

	public event Action OnReady;
	public event Action OnSampleCapacityChanged;
	public event Action<double> OnSignificantSlopeChange; // double: slope.
	public event Action<double> OnQualityChanged; // double: regression quality.

	/// <summary> True when enough samples have been collected for reliable linear regression. </summary>
	public bool IsReady => _samples.IsFull;

	/// <summary> Number of samples currently collected. </summary>
	public int CurrentSampleCount => _samples.Capacity;

	/// <summary> Maximum number of samples used for regression calculation. </summary>
	public int SampleCapacity => _samples.Capacity;

	/// <summary> Progress towards being ready (0.0 to 1.0). </summary>
	public float ReadyProgress => (float)CurrentSampleCount / SampleCapacity;

	/// <summary>
	/// Current slope of the regression line.
	/// Values near 1.0 indicate good time synchronization.
	/// Values significantly different from 1.0 may indicate timing issues.
	/// </summary>
	public double CurrentSlope => _slope;

	/// <summary>
	/// Quality metric for the regression. Higher values indicate better fit.
	/// Range: 0.0 (poor) to 1.0 (perfect fit).
	/// </summary>
	public double RegressionQuality => _regressionQuality;

	private const int MinSampleCount = 15;
	private const int MaxSampleCount = 1000; // Reasonable upper limit to prevent memory issues.
	private const int DefaultSampleCount = 200;
	private const double QualityUpdateThreshold = 0.02; // Update quality when slope changes by this amount.

	// Circular buffers for game time and dsp time samples.
	private CircularBuffer<(double realTime, double dspTime)> _samples;

	private double _previousSmoothDspTime;
	private double _slope;
	private double _intercept;
	private double _regressionQuality;
	private double _lastSlopeForQuality;

	/// <summary>
	/// Creates a new SmoothDspTimeCalculator.
	/// </summary>
	/// <param name="initialRegressionSamples">Initial number of samples to keep for regression. Can be changed later with SetSampleCapacity().</param>
	public SmoothDspTimeCalculator(int initialRegressionSamples = DefaultSampleCount) {
		int validatedCapacity = ValidateSampleCapacity(initialRegressionSamples);

		InitializeRegressionBuffers(validatedCapacity);
		InitializeRegressionData();
	}

	private void InitializeRegressionBuffers(int capacity) {
		_samples = new CircularBuffer<(double realTime, double dspTime)>(capacity);
	}

	private void InitializeRegressionData() {
		_previousSmoothDspTime = AudioSettings.dspTime;
		_intercept = _previousSmoothDspTime - Time.realtimeSinceStartupAsDouble;
		_slope = 1.0;
		_regressionQuality = 0.0;
		_lastSlopeForQuality = _slope;
	}

	/// <summary>
	/// Changes the sample capacity at runtime. This will reset the regression data.
	/// </summary>
	/// <param name="newCapacity">New sample capacity. Must be between MinSampleCount and MaxSampleCount.</param>
	/// <param name="preserveData">If true, attempts to preserve existing samples when increasing capacity.</param>
	public void SetSampleCapacity(int newCapacity, bool preserveData = true) {
		int validatedCapacity = ValidateSampleCapacity(newCapacity);

		if (validatedCapacity == SampleCapacity) {
			return; // No change needed.
		}

		bool wasReady = IsReady;

		if (preserveData && CurrentSampleCount > 0) {
			ResizeBuffersPreservingData(validatedCapacity);
		}
		else {
			InitializeRegressionBuffers(validatedCapacity);
			InitializeRegressionData();
		}

		OnSampleCapacityChanged?.Invoke();

		if (wasReady != IsReady) {
			if (IsReady) {
				OnReady?.Invoke();
			}
		}
	}

	/// <summary>
	/// Resets all regression data, clearing samples and starting fresh.
	/// Useful when you suspect the data has become corrupted or want a clean start.
	/// </summary>
	public void Reset() {
		_samples.Clear();
		InitializeRegressionData();
	}

	/// <summary>
	/// Gets recommended sample capacity based on target adaptation time.
	/// </summary>
	/// <param name="targetAdaptationTime">How quickly you want the regression to adapt to changes.</param>
	/// <param name="updateFrequency">How many times per second UpdateLinearRegression() is called (usually 60+ for games (fps)).</param>
	/// <returns>Recommended sample capacity.</returns>
	public static int GetRecommendedSampleCapacity(float targetAdaptationTime, float updateFrequency = 60.0f) {
		if (targetAdaptationTime <= 0 || updateFrequency <= 0) {
			return DefaultSampleCount;
		}

		int recommended = (int)floor(targetAdaptationTime * updateFrequency);
		return clamp(recommended, MinSampleCount, MaxSampleCount);
	}

	public void UpdateLinearRegression() {
		double realTimeSample = Time.realtimeSinceStartupAsDouble;
		double dspTimeSample = AudioSettings.dspTime;

		bool wasReady = IsReady;

		_samples.PushBack((realTimeSample, dspTimeSample));

		if (!wasReady && IsReady) {
			OnReady?.Invoke();
		}

		if (!IsReady) {
			return;
		}

		double previousSlope = _slope;
		(_slope, _intercept) = CalculateLinearRegression(_samples);

		UpdateRegressionQuality();
		CheckForSignificantSlopeChange(previousSlope);
	}

	/// <summary>
	/// Performs linear regression on the collected time samples.
	///
	/// Math explanation:
	/// - We're finding the best-fit line: dspTime = slope * realTime + intercept.
	/// - Slope ≈ 1.0 means times advance at same rate.
	/// - Slope > 1.0 means DSP time runs faster than real time.
	/// - Intercept is the offset between the two time systems.
	/// </summary>
	/// <param name="samples">X-axis data (real time samples)</param>
	/// <param name="dspTimeSamples">Y-axis data (DSP time samples)</param>
	/// <returns>Tuple of (slope, intercept) for the best-fit line</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static (double slope, double intercept) CalculateLinearRegression(CircularBuffer<(double realTime, double dspTime)> samples) {

		int totalSamples = samples.Capacity;

		double sumX = 0.0;
		double sumY = 0.0;

		// Calculate sums for mean.
		for (int i = 0; i < totalSamples; i++) {
			sumX += samples[i].realTime;
			sumY += samples[i].dspTime;
		}

		double meanX = sumX / totalSamples;
		double meanY = sumY / totalSamples;

		double varianceX = 0.0;
		double covariance = 0.0;

		// Calculate variance and covariance, considering the circular buffer.
		for (int i = 0; i < totalSamples; i++) {
			double dx = samples[i].realTime - meanX;
			double dy = samples[i].dspTime - meanY;

			varianceX += dx * dx;
			covariance += dx * dy;
		}

		if (varianceX == 0.0) {
			// If all X values are the same, the slope is undefined or infinite.
			return (1.0, meanY - meanX);
		}

		double slope = covariance / varianceX;
		double intercept = meanY - slope * meanX;

		return (slope, intercept);
	}

	private void ResizeBuffersPreservingData(int newCapacity) {
		CircularBuffer<(double realTime, double dspTime)> oldSamples = _samples;

		InitializeRegressionBuffers(newCapacity);

		// Copy existing samples to new buffers.
		int samplesToCopy = min(oldSamples.Capacity, newCapacity);

		if (samplesToCopy < 0) {
			return;
		}

		// Copy the most recent samples.
		int startIndex = max(0, oldSamples.Capacity - samplesToCopy);

		for (int i = 0; i < samplesToCopy; i++) {
			int sourceIndex = startIndex + i;

			_samples.PushBack(oldSamples[sourceIndex]);
		}

		// Recalculate regression if we have enough samples.
		if (this.IsReady) {
			(_slope, _intercept) = CalculateLinearRegression(_samples);
			UpdateRegressionQuality();
		}
	}

	private int ValidateSampleCapacity(int requestedCapacity) {
		if (requestedCapacity < MinSampleCount) {
			Debug.LogWarning($"Sample capacity must be at least {MinSampleCount}. Using {MinSampleCount}.");
			return MinSampleCount;
		}

		if (requestedCapacity > MaxSampleCount) {
			Debug.LogWarning($"Sample capacity cannot exceed {MaxSampleCount}. Using {MaxSampleCount}.");
			return MaxSampleCount;
		}

		return requestedCapacity;
	}


	private void UpdateRegressionQuality() {
		// Calculate accuracy: how close slope is to ideal (1.0).
		double slopeDeviation = abs(_slope - 1.0);
		double accuracyScore = max(0.0, 1.0 - slopeDeviation / 0.1); // 0.1 = 10% tolerance.

		// Calculate stability: how much slope changed since last update.
		double slopeChange = abs(_slope - _lastSlopeForQuality);
		double stabilityScore = slopeChange < 0.001 ? 1.0 : max(0.0, 1.0 - slopeChange / 0.01); // 1% tolerance.

		// Combined quality: weighted average (accuracy more important than short-term stability).
		const double accuracyWeight = 0.7;
		const double stabilityWeight = 0.3;
		double newQuality = (accuracyScore * accuracyWeight) + (stabilityScore * stabilityWeight);

		if (abs(newQuality - _regressionQuality) > 0.05) { // 5% change threshold.
			_regressionQuality = newQuality;
			OnQualityChanged?.Invoke(_regressionQuality);
		}

		_lastSlopeForQuality = _slope;
	}

	private void CheckForSignificantSlopeChange(double previousSlope) {
		if (abs(_slope - previousSlope) > QualityUpdateThreshold) {
			OnSignificantSlopeChange?.Invoke(_slope);
		}
	}

	/// <summary>
	/// Gets a status summary for debugging or UI display.
	/// </summary>
	public RegressionStatus GetRegressionStatus() {
		return new RegressionStatus(
			isReady: IsReady,
			progress: ReadyProgress,
			currentSampleCount: CurrentSampleCount,
			sampleCapacity: SampleCapacity,
			slope: CurrentSlope,
			quality: RegressionQuality,
			qualityDescription: GetQualityDescription()
		);
	}

	private string GetQualityDescription() {
		return _regressionQuality switch {
			>= 0.99 => "Perfect",
			>= 0.9 => "Excellent",
			>= 0.7 => "Good",
			>= 0.5 => "Medium",
			>= 0.3 => "Poor",
			_ => "Very Poor"
		};
	}
}

/// <summary>
/// Status information for the regression calculator.
/// </summary>
public readonly struct RegressionStatus {
	public readonly bool IsReady;
	public readonly float Progress;
	public readonly int CurrentSampleCount;
	public readonly int SampleCapacity;
	public readonly double Slope;
	public readonly double Quality;
	public readonly string QualityDescription;

	public RegressionStatus(bool isReady, float progress, int currentSampleCount, int sampleCapacity, double slope, double quality, string qualityDescription) {
		IsReady = isReady;
		Progress = progress;
		CurrentSampleCount = currentSampleCount;
		SampleCapacity = sampleCapacity;
		Slope = slope;
		Quality = quality;
		QualityDescription = qualityDescription;
	}

	public override string ToString() {
		return $"Regression: {(IsReady ? "Ready" : $"{Progress:P0}")} | Samples: {CurrentSampleCount}/{SampleCapacity} | Slope: {Slope:F3} | Quality: {QualityDescription}";
	}
}
