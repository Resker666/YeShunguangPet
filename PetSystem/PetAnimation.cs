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
    int StartColumn,
    int[] DurationsMs,
    bool Loop)
{
    public int FrameCount => DurationsMs.Length;
}
