# Implementation Plan - Volume Parity Adjustment

## Phase 1: Research & Testing Setup
- [ ] Task: Implementation - Create Baseline Tests
    - [ ] Create `BazaarCompanionWeb.Tests` project if it doesn't exist.
    - [ ] Add `OpportunityScoringServiceTests.cs`.
    - [ ] Implement tests covering current `CalculateVolumeScore` behavior with various ratios (1:1, 2:1, 10:1).
- [ ] Task: Conductor - User Manual Verification 'Phase 1' (Protocol in workflow.md)

## Phase 2: Algorithmic Refinement
- [ ] Task: Implementation - Refine Volume Parity Formula
    - [ ] **Red Phase:** Update tests with new expected (lower) scores for imbalanced ratios.
    - [ ] **Green Phase:** Modify `CalculateVolumeScore` in `OpportunityScoringService.cs` to implement the new parity logic.
    - [ ] **Refactor:** Ensure the code is clean and adheres to C# 14 standards.
- [ ] Task: Conductor - User Manual Verification 'Phase 2' (Protocol in workflow.md)
