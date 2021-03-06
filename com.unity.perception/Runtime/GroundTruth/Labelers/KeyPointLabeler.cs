using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;

namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    /// Produces keypoint annotations for a humanoid model. This labeler supports generic
    /// <see cref="KeyPointTemplate"/>. Template values are mapped to rigged
    /// <see cref="Animator"/> <seealso cref="Avatar"/>. Custom joints can be
    /// created by applying <see cref="JointLabel"/> to empty game objects at a body
    /// part's location.
    /// </summary>
    [Serializable]
    public sealed class KeyPointLabeler : CameraLabeler
    {
        /// <summary>
        /// The active keypoint template. Required to annotate keypoint data.
        /// </summary>
        public KeyPointTemplate activeTemplate;

        /// <inheritdoc/>
        public override string description
        {
            get => "Produces keypoint annotations for all visible labeled objects that have a humanoid animation avatar component.";
            protected set { }
        }

        ///<inheritdoc/>
        protected override bool supportsVisualization => true;

        // ReSharper disable MemberCanBePrivate.Global
        /// <summary>
        /// The GUID id to associate with the annotations produced by this labeler.
        /// </summary>
        public string annotationId = "8b3ef246-daa7-4dd5-a0e8-a943f6e7f8c2";
        /// <summary>
        /// The <see cref="IdLabelConfig"/> which associates objects with labels.
        /// </summary>
        public IdLabelConfig idLabelConfig;
        // ReSharper restore MemberCanBePrivate.Global

        AnnotationDefinition m_AnnotationDefinition;
        EntityQuery m_EntityQuery;
        Texture2D m_MissingTexture;

        /// <summary>
        /// Action that gets triggered when a new frame of key points are computed.
        /// </summary>
        public event Action<List<KeyPointEntry>> KeyPointsComputed;

        /// <summary>
        /// Creates a new key point labeler. This constructor creates a labeler that
        /// is not valid until a <see cref="IdLabelConfig"/> and <see cref="KeyPointTemplate"/>
        /// are assigned.
        /// </summary>
        public KeyPointLabeler() { }

        /// <summary>
        /// Creates a new key point labeler.
        /// </summary>
        /// <param name="config">The Id label config for the labeler</param>
        /// <param name="template">The active keypoint template</param>
        public KeyPointLabeler(IdLabelConfig config, KeyPointTemplate template)
        {
            this.idLabelConfig = config;
            this.activeTemplate = template;
        }

        /// <summary>
        /// Array of animation pose labels which map animation clip times to ground truth pose labels.
        /// </summary>
        public AnimationPoseLabel[] poseStateConfigs;

        /// <inheritdoc/>
        protected override void Setup()
        {
            if (idLabelConfig == null)
                throw new InvalidOperationException("KeyPointLabeler's idLabelConfig field must be assigned");

            m_AnnotationDefinition = DatasetCapture.RegisterAnnotationDefinition("keypoints", new []{TemplateToJson(activeTemplate)},
                "pixel coordinates of keypoints in a model, along with skeletal connectivity data", id: new Guid(annotationId));

            m_EntityQuery = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(typeof(Labeling), typeof(GroundTruthInfo));

            m_KeyPointEntries = new List<KeyPointEntry>();

            // Texture to use in case the template does not contain a texture for the joints or the skeletal connections
            m_MissingTexture = new Texture2D(1, 1);

            m_KnownStatus = new Dictionary<uint, CachedData>();
        }

        /// <inheritdoc/>
        protected override void OnBeginRendering()
        {
            var reporter = perceptionCamera.SensorHandle.ReportAnnotationAsync(m_AnnotationDefinition);

            var entities = m_EntityQuery.ToEntityArray(Allocator.TempJob);
            var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            m_KeyPointEntries.Clear();

            foreach (var entity in entities)
            {
                ProcessEntity(entityManager.GetComponentObject<Labeling>(entity));
            }

            entities.Dispose();

            KeyPointsComputed?.Invoke(m_KeyPointEntries);
            reporter.ReportValues(m_KeyPointEntries);
        }

        // ReSharper disable InconsistentNaming
        // ReSharper disable NotAccessedField.Global
        // ReSharper disable NotAccessedField.Local
        /// <summary>
        /// Record storing all of the keypoint data of a labeled gameobject.
        /// </summary>
        [Serializable]
        public class KeyPointEntry
        {
            /// <summary>
            /// The label id of the entity
            /// </summary>
            public int label_id;
            /// <summary>
            /// The instance id of the entity
            /// </summary>
            public uint instance_id;
            /// <summary>
            /// The template that the points are based on
            /// </summary>
            public string template_guid;
            /// <summary>
            /// Pose ground truth for the current set of keypoints
            /// </summary>
            public string pose = "unset";
            /// <summary>
            /// Array of all of the keypoints
            /// </summary>
            public KeyPoint[] keypoints;
        }

        /// <summary>
        /// The values of a specific keypoint
        /// </summary>
        [Serializable]
        public class KeyPoint
        {
            /// <summary>
            /// The index of the keypoint in the template file
            /// </summary>
            public int index;
            /// <summary>
            /// The keypoint's x-coordinate pixel location
            /// </summary>
            public float x;
            /// <summary>
            /// The keypoint's y-coordinate pixel location
            /// </summary>
            public float y;
            /// <summary>
            /// The state of the point, 0 = not present, 1 = keypoint is present
            /// </summary>
            public int state;
        }
        // ReSharper restore InconsistentNaming
        // ReSharper restore NotAccessedField.Global
        // ReSharper restore NotAccessedField.Local

        // Converts a coordinate from world space into pixel space
        Vector3 ConvertToScreenSpace(Vector3 worldLocation)
        {
            var pt = perceptionCamera.attachedCamera.WorldToScreenPoint(worldLocation);
            pt.y = Screen.height - pt.y;
            return pt;
        }

        List<KeyPointEntry> m_KeyPointEntries;

        struct CachedData
        {
            public bool status;
            public Animator animator;
            public KeyPointEntry keyPoints;
            public List<(JointLabel, int)> overrides;
        }

        Dictionary<uint, CachedData> m_KnownStatus;

        bool TryToGetTemplateIndexForJoint(KeyPointTemplate template, JointLabel joint, out int index)
        {
            index = -1;

            foreach (var jointTemplate in joint.templateInformation.Where(jointTemplate => jointTemplate.template == template))
            {
                for (var i = 0; i < template.keyPoints.Length; i++)
                {
                    if (template.keyPoints[i].label == jointTemplate.label)
                    {
                        index = i;
                        return true;
                    }
                }
            }

            return false;
        }

        bool DoesTemplateContainJoint(JointLabel jointLabel)
        {
            foreach (var template in jointLabel.templateInformation)
            {
                if (template.template == activeTemplate)
                {
                    if (activeTemplate.keyPoints.Any(i => i.label == template.label))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        void ProcessEntity(Labeling labeledEntity)
        {
            // Cache out the data of a labeled game object the first time we see it, this will
            // save performance each frame. Also checks to see if a labeled game object can be annotated.
            if (!m_KnownStatus.ContainsKey(labeledEntity.instanceId))
            {
                var cached = new CachedData()
                {
                    status = false,
                    animator = null,
                    keyPoints = new KeyPointEntry(),
                    overrides = new List<(JointLabel, int)>()
                };

                if (idLabelConfig.TryGetLabelEntryFromInstanceId(labeledEntity.instanceId, out var labelEntry))
                {
                    var entityGameObject = labeledEntity.gameObject;

                    cached.keyPoints.instance_id = labeledEntity.instanceId;
                    cached.keyPoints.label_id = labelEntry.id;
                    cached.keyPoints.template_guid = activeTemplate.templateID.ToString();

                    cached.keyPoints.keypoints = new KeyPoint[activeTemplate.keyPoints.Length];
                    for (var i = 0; i < cached.keyPoints.keypoints.Length; i++)
                    {
                        cached.keyPoints.keypoints[i] = new KeyPoint { index = i, state = 0 };
                    }

                    var animator = entityGameObject.transform.GetComponentInChildren<Animator>();
                    if (animator != null)
                    {
                        cached.animator = animator;
                        cached.status = true;
                    }

                    foreach (var joint in entityGameObject.transform.GetComponentsInChildren<JointLabel>())
                    {
                        if (TryToGetTemplateIndexForJoint(activeTemplate, joint, out var idx))
                        {
                            cached.overrides.Add((joint, idx));
                            cached.status = true;
                        }
                    }
                }

                m_KnownStatus[labeledEntity.instanceId] = cached;
            }

            var cachedData = m_KnownStatus[labeledEntity.instanceId];

            if (cachedData.status)
            {
                var animator = cachedData.animator;
                var keyPoints = cachedData.keyPoints.keypoints;

                // Go through all of the rig keypoints and get their location
                for (var i = 0; i < activeTemplate.keyPoints.Length; i++)
                {
                    var pt = activeTemplate.keyPoints[i];
                    if (pt.associateToRig)
                    {
                        var bone = animator.GetBoneTransform(pt.rigLabel);
                        if (bone != null)
                        {
                            var loc = ConvertToScreenSpace(bone.position);
                            keyPoints[i].index = i;
                            keyPoints[i].x = loc.x;
                            keyPoints[i].y = loc.y;
                            keyPoints[i].state = 2;
                        }
                    }
                }

                // Go through all of the additional or override points defined by joint labels and get
                // their locations
                foreach (var (joint, idx) in cachedData.overrides)
                {
                    var loc = ConvertToScreenSpace(joint.transform.position);
                    keyPoints[idx].index = idx;
                    keyPoints[idx].x = loc.x;
                    keyPoints[idx].y = loc.y;
                    keyPoints[idx].state = 1;
                }

                cachedData.keyPoints.pose = "unset";

                if (cachedData.animator != null)
                {
                    cachedData.keyPoints.pose = GetPose(cachedData.animator);
                }

                m_KeyPointEntries.Add(cachedData.keyPoints);
            }
        }

        string GetPose(Animator animator)
        {
            var info = animator.GetCurrentAnimatorClipInfo(0);

            if (info != null && info.Length > 0)
            {
                var clip = info[0].clip;
                var timeOffset = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;

                if (poseStateConfigs != null)
                {
                    foreach (var p in poseStateConfigs)
                    {
                        if (p.animationClip == clip)
                        {
                            var time = timeOffset;
                            var label = p.GetPoseAtTime(time);
                            return label;
                        }
                    }
                }
            }

            return "unset";
        }

        /// <inheritdoc/>
        protected override void OnVisualize()
        {
            var jointTexture = activeTemplate.jointTexture;
            if (jointTexture == null) jointTexture = m_MissingTexture;

            var skeletonTexture = activeTemplate.skeletonTexture;
            if (skeletonTexture == null) skeletonTexture = m_MissingTexture;

            foreach (var entry in m_KeyPointEntries)
            {
                foreach (var bone in activeTemplate.skeleton)
                {
                    var joint1 = entry.keypoints[bone.joint1];
                    var joint2 = entry.keypoints[bone.joint2];

                    if (joint1.state != 0 && joint2.state != 0)
                    {
                        VisualizationHelper.DrawLine(joint1.x, joint1.y, joint2.x, joint2.y, bone.color, 8, skeletonTexture);
                    }
                }

                foreach (var keypoint in entry.keypoints)
                {
                    if (keypoint.state != 0)
                        VisualizationHelper.DrawPoint(keypoint.x, keypoint.y, activeTemplate.keyPoints[keypoint.index].color, 8, jointTexture);
                }
            }
        }

        // ReSharper disable InconsistentNaming
        // ReSharper disable NotAccessedField.Local
        [Serializable]
        struct JointJson
        {
            public string label;
            public int index;
        }

        [Serializable]
        struct SkeletonJson
        {
            public int joint1;
            public int joint2;
        }

        [Serializable]
        struct KeyPointJson
        {
            public string template_id;
            public string template_name;
            public JointJson[] key_points;
            public SkeletonJson[] skeleton;
        }
        // ReSharper restore InconsistentNaming
        // ReSharper restore NotAccessedField.Local

        KeyPointJson TemplateToJson(KeyPointTemplate input)
        {
            var json = new KeyPointJson();
            json.template_id = input.templateID.ToString();
            json.template_name = input.templateName;
            json.key_points = new JointJson[input.keyPoints.Length];
            json.skeleton = new SkeletonJson[input.skeleton.Length];

            for (var i = 0; i < input.keyPoints.Length; i++)
            {
                json.key_points[i] = new JointJson
                {
                    label = input.keyPoints[i].label,
                    index = i
                };
            }

            for (var i = 0; i < input.skeleton.Length; i++)
            {
                json.skeleton[i] = new SkeletonJson()
                {
                    joint1 = input.skeleton[i].joint1,
                    joint2 = input.skeleton[i].joint2
                };
            }

            return json;
        }
    }
}
