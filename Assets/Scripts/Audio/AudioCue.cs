namespace LustHouse.Audio
{
    /// <summary>
    /// The sounds the game can make.
    ///
    /// One vocabulary for the whole game, so a cue means the same thing wherever it is
    /// played. A reel stop is a reel stop in the slot screen and in any feature round
    /// that spins reels; it is not "a sound the slot presenter happens to own".
    ///
    /// The cues are deliberately few. Every one of them has to be worth hearing: a cue
    /// that fires on every third tap is noise, and noise trains the player to stop
    /// listening.
    /// </summary>
    public enum AudioCue
    {
        /// <summary>Nothing. The default, so an unset cue plays silence.</summary>
        None = 0,

        /// <summary>A button was pressed.</summary>
        UiClick,

        /// <summary>The spin button was pressed. Heavier than a plain click: this is the
        /// one press that spends the player's money.</summary>
        SpinPress,

        /// <summary>A game has opened and its content is ready.</summary>
        GameWelcome,

        /// <summary>The reels are turning. Loops until they stop.</summary>
        ReelSpinLoop,

        /// <summary>One reel has landed.</summary>
        ReelStop,

        /// <summary>A small payout.</summary>
        WinSmall,

        /// <summary>A payout worth a fanfare.</summary>
        WinMedium,

        /// <summary>A big payout.</summary>
        WinBig,

        /// <summary>A progressive jackpot has been won.</summary>
        JackpotWin,

        /// <summary>A feature round has been triggered.</summary>
        BonusTrigger,

        /// <summary>
        /// A bed of background music, on the music bus rather than the sfx one.
        ///
        /// Music is not a sound effect played loudly: it has its own channel, its own
        /// volume in the player's settings, and its own fade, so leaving the room it
        /// belongs to takes it down gently rather than cutting it off.
        /// </summary>
        LobbyMusic,

        /// <summary>
        /// The game screen's own bed, and a different room from the lobby's.
        ///
        /// It comes up when the reels start turning and fades away a few seconds after
        /// they stop: a machine that is being played has a pulse, and a machine that is
        /// being looked at should not keep making the player feel like they are late
        /// for something. The lobby bed stays on the lobby's channel and the two never
        /// play at once.
        /// </summary>
        SlotMusic,
    }
}
