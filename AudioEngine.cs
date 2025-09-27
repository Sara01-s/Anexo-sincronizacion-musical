using static Unity.Mathematics.math;
using UnityEngine;

/// <summary>
/// Manages precise audio timing for rhythm games using smooth DSP time calculations.
///
/// Problem: Rhythm games require extremely precise timing to synchronize gameplay with music.
/// Unity's AudioSource.time and AudioSettings.dspTime can have inconsistencies, jitter, and drift
/// that cause notes to appear out of sync with the actual audio playback. This is especially
/// problematic in competitive rhythm games where millisecond precision is crucial.
///
/// Solution: This engine uses a SmoothDspTimeCalculator to create stable, jitter-free timing
/// that automatically compensates for audio system latency and detects/corrects timing drift
/// between different audio time sources.
///
/// Features:
/// - Smooth, monotonic song time calculation without backwards jumps
/// - Automatic audio latency compensation based on buffer settings
/// - Drift detection and correction between DSP time and AudioSource time
/// - Configurable drift tolerance for different precision requirements
///
/// Usage:
/// 1. Create with a configured SmoothDspTimeCalculator
/// 2. Call UpdateSmoothDspTime() every frame with AudioSource.time
/// 3. Use GetChartSongTime() to get precise timing for note spawning/judging
/// </summary>
public class AudioEngine {
	private readonly SmoothDspTimeCalculator _smoothDspTimeCalculator;

	/// <summary>
	/// Creates a new AudioEngine instance.
	/// </summary>
	/// <param name="smoothDspTimeCalculator">Calculator for smooth DSP time regression. Should be pre-configured and updating.</param>
	public AudioEngine(SmoothDspTimeCalculator smoothDspTimeCalculator) {
		_smoothDspTimeCalculator = smoothDspTimeCalculator;
	}

	/// <summary>
	/// Calculates the current audio time given a smoothed dsp time input.
	/// This is the primary timing value used for spawning notes and judging player input.
	/// </summary>
	/// <param name="audioStartDspTime">The DSP time when the song started playing.</param>
	/// <param name="secondsToFirstBeat">Offset from song start to the first beat (song's initial silence/intro).</param>
	/// <param name="latencyCompensation">Additional user-configured latency compensation in seconds.</param>
	/// <returns>Current audio time in seconds, accounting for all timing corrections and latencies.</returns>
	public double GetAudioTime(double audioStartDspTime, double secondsToFirstBeat, double latencyCompensation) {
		double smoothDspTime = _smoothDspTimeCalculator.SmoothDspTime;
		double songTime = smoothDspTime - audioStartDspTime
										- secondsToFirstBeat
										- latencyCompensation
										- GetEstimatedLatency();
		return audioTime;
	}

	/// <summary>
	/// Updates the smooth DSP time calculation and performs drift correction.
	/// Should be called every frame during active audio playback.
	/// </summary>
	/// <param name="audioSourceTime">Current AudioSource.time value for drift comparison.</param>
	/// <param name="songStartDspTime">Reference to song start time that may be adjusted for drift correction.</param>
	public void UpdateSmoothDspTime(double audioSourceTime, ref double songStartDspTime) {
		_smoothDspTimeCalculator.UpdateLinearRegression();

		double smoothDspTime = _smoothDspTimeCalculator.SmoothDspTime;

		CheckForDrifts(ref songStartDspTime, smoothDspTime, audioSourceTime, timingDriftWindowMs: 50);
	}

	/// <summary>
	/// Estimates the current audio system latency based on Unity's audio buffer configuration.
	/// This latency represents the time between when audio is processed and when it's actually heard.
	/// </summary>
	/// <returns>Estimated audio latency in seconds.</returns>
	private double GetEstimatedLatency() {
		AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
		int sampleRate = AudioSettings.GetConfiguration().sampleRate;
		double estimatedLatency = (double)(bufferLength * numBuffers) / sampleRate;

		return estimatedLatency;
	}

	/// <summary>
	/// Detects and corrects timing drift between smooth DSP time and AudioSource time.
	///
	/// Timing drift can occur due to:
	/// - Audio driver inconsistencies
	/// - System performance variations
	/// - Different timing sources becoming desynchronized
	///
	/// When significant drift is detected, the song start time is adjusted to maintain
	/// synchronization between the two timing sources.
	/// </summary>
	/// <param name="songStartDspTime">Reference to song start time that will be modified if drift is detected.</param>
	/// <param name="smoothDspTime">Current smooth DSP time from the calculator.</param>
	/// <param name="audioSourceTime">Current AudioSource.time for comparison.</param>
	/// <param name="timingDriftWindowMs">Drift tolerance in milliseconds before correction is applied.</param>
	private void CheckForDrifts(ref double songStartDspTime, double smoothDspTime, double audioSourceTime, double timingDriftWindowMs) {
		double timeFromDsp = smoothDspTime - songStartDspTime;
		double timeFromAudioSource = audioSourceTime;
		double timeDiff = timeFromDsp - timeFromAudioSource;

		if (abs(timeDiff) > timingDriftWindowMs + double.Epsilon) {
			songStartDspTime += timeDiff;
		}
	}
}

