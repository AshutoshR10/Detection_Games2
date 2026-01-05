using UnityEngine;
using System.Collections.Generic;

namespace PoseInteraction.Utilities
{
    /// <summary>
    /// Tracks velocity and acceleration of hand movement with optimized calculations
    /// Uses circular buffer for efficient memory management
    /// </summary>
    public class HandVelocityTracker
    {
        private readonly int _sampleSize;
        private readonly Queue<Vector3> _positionHistory;
        private readonly Queue<float> _timeHistory;

        private Vector3 _lastPosition;
        private float _lastTime;

        public Vector3 CurrentVelocity { get; private set; }
        public Vector3 CurrentAcceleration { get; private set; }
        public float Speed { get; private set; }

        /// <summary>
        /// Initialize velocity tracker
        /// </summary>
        /// <param name="sampleSize">Number of samples to average (3-5 recommended)</param>
        public HandVelocityTracker(int sampleSize = 5)
        {
            _sampleSize = Mathf.Max(2, sampleSize);
            _positionHistory = new Queue<Vector3>(_sampleSize);
            _timeHistory = new Queue<float>(_sampleSize);

            _lastPosition = Vector3.zero;
            _lastTime = 0f;
            CurrentVelocity = Vector3.zero;
            CurrentAcceleration = Vector3.zero;
            Speed = 0f;
        }

        /// <summary>
        /// Update with new position data
        /// </summary>
        public void UpdatePosition(Vector3 position, float currentTime)
        {
            // Add to history
            _positionHistory.Enqueue(position);
            _timeHistory.Enqueue(currentTime);

            // Remove oldest if exceeded sample size
            if (_positionHistory.Count > _sampleSize)
            {
                _positionHistory.Dequeue();
                _timeHistory.Dequeue();
            }

            // Calculate velocity if we have enough samples
            if (_positionHistory.Count >= 2)
            {
                Vector3 oldVelocity = CurrentVelocity;
                CurrentVelocity = CalculateVelocity();
                Speed = CurrentVelocity.magnitude;

                // Calculate acceleration (change in velocity)
                if (_lastTime > 0)
                {
                    float deltaTime = currentTime - _lastTime;
                    if (deltaTime > 0.001f)
                    {
                        CurrentAcceleration = (CurrentVelocity - oldVelocity) / deltaTime;
                    }
                }
            }

            _lastPosition = position;
            _lastTime = currentTime;
        }

        /// <summary>
        /// Calculate average velocity from position history
        /// </summary>
        private Vector3 CalculateVelocity()
        {
            if (_positionHistory.Count < 2)
                return Vector3.zero;

            var positions = _positionHistory.ToArray();
            var times = _timeHistory.ToArray();

            Vector3 totalVelocity = Vector3.zero;
            int validSamples = 0;

            for (int i = 1; i < positions.Length; i++)
            {
                float deltaTime = times[i] - times[i - 1];
                if (deltaTime > 0.001f) // Avoid division by very small numbers
                {
                    Vector3 displacement = positions[i] - positions[i - 1];
                    totalVelocity += displacement / deltaTime;
                    validSamples++;
                }
            }

            return validSamples > 0 ? totalVelocity / validSamples : Vector3.zero;
        }

        /// <summary>
        /// Manually set velocity (useful for correcting mirror mode)
        /// </summary>
        public void SetVelocity(Vector3 velocity)
        {
            CurrentVelocity = velocity;
            Speed = velocity.magnitude;
        }

        /// <summary>
        /// Reset all tracking data
        /// </summary>
        public void Reset()
        {
            _positionHistory.Clear();
            _timeHistory.Clear();
            CurrentVelocity = Vector3.zero;
            CurrentAcceleration = Vector3.zero;
            Speed = 0f;
            _lastPosition = Vector3.zero;
            _lastTime = 0f;
        }

        /// <summary>
        /// Check if hand is moving faster than threshold
        /// </summary>
        public bool IsMovingFast(float threshold)
        {
            return Speed > threshold;
        }

        /// <summary>
        /// Get velocity in a specific direction
        /// </summary>
        public float GetDirectionalVelocity(Vector3 direction)
        {
            return Vector3.Dot(CurrentVelocity, direction.normalized);
        }
    }
}
