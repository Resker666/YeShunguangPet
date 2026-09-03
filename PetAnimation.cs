using System.Collections.Generic;

namespace YeShunguangPet;

public enum PetState
{
    Idle,
    RunningRight,
    RunningLeft,
    Waving,
    Jumping,
    Failed,
    Waiting,
    Running,
    Review
}

public sealed record PetAnimation(
    PetState State,
    int Row,
    int FrameCount,
    int[] DurationsMs,
    bool Loop);

public static class PetAnimations
{
    public const int CellWidth = 192;
    public const int CellHeight = 208;
    public const int Columns = 8;
    public const int Rows = 11;
    public const int LookDirectionCount = 16;

    private static readonly Dictionary<PetState, PetAnimation> Definitions = new()
    {
        [PetState.Idle] = new(PetState.Idle, 0, 6, new[] { 280, 110, 110, 140, 140, 320 }, true),
        [PetState.RunningRight] = new(PetState.RunningRight, 1, 8, new[] { 120, 120, 120, 120, 120, 120, 120, 220 }, true),
        [PetState.RunningLeft] = new(PetState.RunningLeft, 2, 8, new[] { 120, 120, 120, 120, 120, 120, 120, 220 }, true),
        [PetState.Waving] = new(PetState.Waving, 3, 4, new[] { 140, 140, 140, 280 }, false),
        [PetState.Jumping] = new(PetState.Jumping, 4, 5, new[] { 140, 140, 140, 140, 280 }, false),
        [PetState.Failed] = new(PetState.Failed, 5, 8, new[] { 140, 140, 140, 140, 140, 140, 140, 240 }, false),
        [PetState.Waiting] = new(PetState.Waiting, 6, 6, new[] { 150, 150, 150, 150, 150, 260 }, true),
        [PetState.Running] = new(PetState.Running, 7, 6, new[] { 120, 120, 120, 120, 120, 220 }, true),
        [PetState.Review] = new(PetState.Review, 8, 6, new[] { 150, 150, 150, 150, 150, 280 }, true)
    };

    public static PetAnimation Get(PetState state) => Definitions[state];
}
