using HeadphoneAutoSwitch;

// Explicit hardware check: switch to the two supplied endpoint IDs, verify Windows'
// actual defaults, then restore the user's original output even if an assertion fails.
if (args.Length != 2) throw new ArgumentException("Pass headphones and speakers endpoint IDs");
var beforeConsole = WindowsAudio.DefaultId(0);
var beforeMultimedia = WindowsAudio.DefaultId(1);
var beforeCommunications = WindowsAudio.DefaultId(2);
if (beforeConsole == null || beforeConsole != beforeMultimedia)
    throw new InvalidOperationException("Hardware check requires a shared original Console/Multimedia default for exact restoration");

try
{
    foreach (var id in args)
    {
        WindowsAudio.SetDefault(id, false);
        if (WindowsAudio.DefaultId(0) != id || WindowsAudio.DefaultId(1) != id)
            throw new Exception("Windows did not apply the requested output");
        if (WindowsAudio.DefaultId(2) != beforeCommunications)
            throw new Exception("Communications default changed unexpectedly");
        Console.WriteLine("PASS switch: " + WindowsAudio.ListOutputs().Single(x => x.Id == id).Name);
    }
}
finally
{
    WindowsAudio.SetDefault(beforeConsole, false);
    if (WindowsAudio.DefaultId(0) != beforeConsole || WindowsAudio.DefaultId(1) != beforeMultimedia)
        throw new Exception("Could not restore original defaults");
    Console.WriteLine("PASS restored original audio output");
}
