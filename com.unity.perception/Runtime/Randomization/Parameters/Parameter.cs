using System;
using UnityEngine;
using UnityEngine.Perception.Randomization.Parameters.Attributes;
using UnityEngine.Perception.Randomization.Samplers;

namespace UnityEngine.Perception.Randomization.Parameters
{
    /// <summary>
    /// Parameters, in conjunction with a parameter configuration, are used to create convenient interfaces for
    /// randomizing simulations.
    /// </summary>
    [Serializable]
    public abstract class Parameter
    {
        public string name = "Parameter";
        [SerializeField] internal bool collapsed;
        [HideInInspector] public ParameterTarget target = new ParameterTarget();

        public bool hasTarget => target.gameObject != null;

        /// <summary>
        /// Returns meta information regarding this type of parameter
        /// </summary>
        public ParameterMetaData MetaData =>
            (ParameterMetaData)Attribute.GetCustomAttribute(GetType(), typeof(ParameterMetaData));

        /// <summary>
        /// An array containing a reference to each sampler field in this parameter
        /// </summary>
        public abstract ISampler[] samplers { get; }

        /// <summary>
        /// Resets sampler states and then offsets those states using the current scenario iteration
        /// </summary>
        /// <param name="scenarioIteration">The current scenario iteration</param>
        public void ResetState(int scenarioIteration)
        {
            foreach (var sampler in samplers)
                sampler.ResetState(scenarioIteration);
        }

        /// <summary>
        /// The sample type generated by this parameter
        /// </summary>
        public abstract Type OutputType { get; }

        /// <summary>
        /// Applies one sampled value to this parameters assigned target gameobject
        /// </summary>
        public abstract void ApplyToTarget(int seedOffset);

        /// <summary>
        /// Validates parameter settings
        /// </summary>
        public virtual void Validate()
        {
            if (hasTarget)
            {
                if (target.component == null)
                    throw new ParameterException($"Null component target on parameter \"{name}\"");
                if (string.IsNullOrEmpty(target.propertyName))
                    throw new ParameterException($"Invalid property target on parameter \"{name}\"");
            }
        }
    }
}