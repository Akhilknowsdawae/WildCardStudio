using System;
using UnityEngine;

namespace WildCardStudio
{
    public enum EnemyAIBehavior
    {
        Balanced,
        Aggressive,
        Conservative,
        Opportunistic
    }

    [Serializable]
    public sealed class AIPersonality
    {
        public EnemyAIBehavior behavior = EnemyAIBehavior.Balanced;
        [Range(0f, 1f)] public float behaviorStrength = 1f;

        public EnemyAISettings ApplyTo(EnemyAISettings baseSettings)
        {
            if (baseSettings == null)
            {
                throw new ArgumentNullException(nameof(baseSettings));
            }

            EnemyAISettings adjusted = new EnemyAISettings
            {
                futureDrawHorizon = baseSettings.futureDrawHorizon,
                immediatePointsWeight = baseSettings.immediatePointsWeight,
                futurePointsWeight = baseSettings.futurePointsWeight,
                opportunityCostWeight = baseSettings.opportunityCostWeight,
                cardRetentionWeight = baseSettings.cardRetentionWeight,
                drawActionCost = baseSettings.drawActionCost,
                discardPickupPriority = baseSettings.discardPickupPriority,
                comboScoreMultiplier = baseSettings.comboScoreMultiplier
            };

            float immediateMultiplier = 1f;
            float futureMultiplier = 1f;
            float opportunityMultiplier = 1f;
            float retentionMultiplier = 1f;
            float discardMultiplier = 1f;
            switch (behavior)
            {
                case EnemyAIBehavior.Aggressive:
                    immediateMultiplier = 1.3f;
                    futureMultiplier = 0.85f;
                    opportunityMultiplier = 0.8f;
                    retentionMultiplier = 0.75f;
                    break;
                case EnemyAIBehavior.Conservative:
                    immediateMultiplier = 0.9f;
                    futureMultiplier = 1.2f;
                    opportunityMultiplier = 1.25f;
                    retentionMultiplier = 1.35f;
                    discardMultiplier = 0.8f;
                    break;
                case EnemyAIBehavior.Opportunistic:
                    futureMultiplier = 1.15f;
                    retentionMultiplier = 0.9f;
                    discardMultiplier = 1.5f;
                    break;
            }

            adjusted.immediatePointsWeight *= Mathf.Lerp(1f, immediateMultiplier, behaviorStrength);
            adjusted.futurePointsWeight *= Mathf.Lerp(1f, futureMultiplier, behaviorStrength);
            adjusted.opportunityCostWeight *= Mathf.Lerp(1f, opportunityMultiplier, behaviorStrength);
            adjusted.cardRetentionWeight *= Mathf.Lerp(1f, retentionMultiplier, behaviorStrength);
            adjusted.discardPickupPriority *= Mathf.Lerp(1f, discardMultiplier, behaviorStrength);
            return adjusted;
        }
    }
}
