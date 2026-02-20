# Track Specification: Volume Parity Adjustment

## Goal
Refine the `OpportunityScoringService` to more heavily weight the parity between bid and ask volumes. Products with significantly unbalanced volume (e.g., massive bid volume but tiny ask volume) should receive lower opportunity scores, as they represent higher execution risk or potential "spread traps".

## Current State
The `CalculateVolumeScore` method currently uses a linear `balanceFactor`:
```csharp
var ratio = (double)bidMovingWeek / Math.Max(1, askMovingWeek);
var balanceFactor = Math.Min(ratio, 1.0 / ratio);
return throughputScore * executionFactor * (0.6 + 0.4 * balanceFactor);
```
This means even with a total imbalance (balanceFactor = 0), the volume score only drops by 40%.

## Proposed Changes
1. **Increase Parity Weight:** Change the weighting from 60/40 to something more aggressive, e.g., 40/60 or 30/70.
2. **Non-linear Penalty:** Implement a non-linear penalty for extreme imbalances (e.g., using a power function or a steeper sigmoid) so that a 10:1 imbalance is penalized much more heavily than a 2:1 imbalance.
3. **Thresholds:** Consider a minimum parity threshold below which the score drops off sharply.

## Acceptance Criteria
- `OpportunityScoringService` tests reflect the new parity weighting.
- Products with balanced volumes (1:1 ratio) retain high scores.
- Products with extreme imbalances (e.g. >10:1 ratio) see a significant reduction in their volume score and overall opportunity rating.
