using System;

namespace YeShunguangPet;

public sealed class SessionVisuals
{
    private readonly TimeProvider _clock;
    private long? _requestedAt;
    private long? _playingAt;

    public SessionVisuals(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public bool IsCompletionActive => _playingAt.HasValue &&
        _clock.GetElapsedTime(_playingAt.Value) < TimeSpan.FromSeconds(3);

    public void RequestCompletion()
    {
        _requestedAt = _clock.GetTimestamp();
        _playingAt = null;
    }

    public void ClearCompletion() => _requestedAt = _playingAt = null;

    public static PetState RestingState(CompanionSession session, PetSettings settings, PetPackage pet, bool quiet)
        => session.IsFocusing && settings.SessionAnimationEnabled && !quiet && pet.Supports(PetState.Running)
            ? PetState.Running : PetState.Idle;

    public PetState? Resolve(CompanionSession session, PetSettings settings, PetPackage pet, bool quiet, bool canAnimate)
    {
        if (session.Status != SessionStatus.Completed || !settings.SessionAnimationEnabled ||
            !settings.NotificationsEnabled || quiet) ClearCompletion();
        if (_requestedAt.HasValue && _clock.GetElapsedTime(_requestedAt.Value) >= TimeSpan.FromSeconds(10))
            _requestedAt = null;
        if (!canAnimate) return null;
        if (_requestedAt.HasValue)
        {
            _playingAt = _clock.GetTimestamp();
            _requestedAt = null;
        }
        if (IsCompletionActive)
            return pet.Supports(PetState.Waving) ? PetState.Waving :
                pet.Supports(PetState.Jumping) ? PetState.Jumping : PetState.Idle;
        return RestingState(session, settings, pet, quiet);
    }
}
