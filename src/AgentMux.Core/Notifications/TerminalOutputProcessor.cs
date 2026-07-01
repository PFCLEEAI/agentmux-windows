using System.Text;

namespace AgentMux.Core.Notifications;

public sealed class TerminalOutputProcessor
{
    private const char Escape = '\u001b';
    private const char Bell = '\u0007';

    private readonly StringBuilder _visibleText = new();
    private readonly StringBuilder _oscPayload = new();
    private readonly List<OscEvent> _events = [];
    private bool _inOsc;
    private bool _pendingEscape;

    public TerminalOutputChunk Process(string? text)
    {
        return ProcessCore(text, flush: false);
    }

    public TerminalOutputChunk Flush()
    {
        return ProcessCore(null, flush: true);
    }

    private TerminalOutputChunk ProcessCore(string? text, bool flush)
    {
        _visibleText.Clear();
        _events.Clear();

        if (!string.IsNullOrEmpty(text))
        {
            foreach (var character in text)
            {
                ProcessCharacter(character);
            }
        }

        if (flush)
        {
            if (!_inOsc && _pendingEscape)
            {
                _visibleText.Append(Escape);
            }

            _pendingEscape = false;
            _inOsc = false;
            _oscPayload.Clear();
        }

        return new TerminalOutputChunk(_visibleText.ToString(), _events.ToArray());
    }

    private void ProcessCharacter(char character)
    {
        if (_inOsc)
        {
            ProcessOscCharacter(character);
            return;
        }

        if (_pendingEscape)
        {
            if (character == ']')
            {
                _pendingEscape = false;
                _inOsc = true;
                _oscPayload.Clear();
                return;
            }

            _visibleText.Append(Escape);
            _visibleText.Append(character);
            _pendingEscape = false;
            return;
        }

        if (character == Escape)
        {
            _pendingEscape = true;
            return;
        }

        _visibleText.Append(character);
    }

    private void ProcessOscCharacter(char character)
    {
        if (_pendingEscape)
        {
            if (character == '\\')
            {
                CompleteOsc();
                return;
            }

            _oscPayload.Append(Escape);
            _pendingEscape = false;
        }

        if (character == Bell)
        {
            CompleteOsc();
            return;
        }

        if (character == Escape)
        {
            _pendingEscape = true;
            return;
        }

        _oscPayload.Append(character);
    }

    private void CompleteOsc()
    {
        var parsed = OscNotificationParser.Parse(_oscPayload.ToString());
        if (parsed.Kind != OscEventKind.Unknown)
        {
            _events.Add(parsed);
        }

        _oscPayload.Clear();
        _pendingEscape = false;
        _inOsc = false;
    }
}

public sealed record TerminalOutputChunk(string Text, IReadOnlyList<OscEvent> Events);
