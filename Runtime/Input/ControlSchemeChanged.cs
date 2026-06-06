namespace Mosaic.UI
{
    /// <summary>
    /// Message published on the <see cref="EventBus"/> (<c>MosaicUI.Events</c>) by <see cref="InputService"/>
    /// when the active input control scheme switches — for example when the player stops using the
    /// keyboard/mouse and picks up a gamepad. Consumers subscribe via
    /// <c>MosaicUI.Events.Subscribe&lt;ControlSchemeChanged&gt;(...)</c> to adapt prompts, glyphs, or layout
    /// to the new device class.
    /// <para>
    /// <see cref="Previous"/> is the scheme name that was active before the switch (may be <c>null</c> if no
    /// scheme was previously active); <see cref="Current"/> is the scheme name now active (may be <c>null</c>
    /// if the user became unpaired / no scheme is active). The names match the control-scheme names declared
    /// in the assigned <see cref="UnityEngine.InputSystem.InputActionAsset"/>.
    /// </para>
    /// </summary>
    public struct ControlSchemeChanged
    {
        /// <summary>The control-scheme name active before the switch, or <c>null</c> if none was active.</summary>
        public string Previous;

        /// <summary>The control-scheme name active after the switch, or <c>null</c> if none is active.</summary>
        public string Current;
    }
}
