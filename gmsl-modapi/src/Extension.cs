using UndertaleModLib;

namespace GMSL;

public static class Extension
{

    private static List<uint> _takenIds = new();
    private static uint _currentId = 0;

    public static void Init(UndertaleData data)
    {
        foreach (var extension in data.Extensions)
        {
            foreach (var file in extension.Files)
            {
                foreach (var function in file.Functions)
                {
                    _takenIds.Add(function.ID);
                }
            }
        }
    }

    public static uint NextId()
    {
        while (_takenIds.Contains(_currentId))
        {
            _currentId++;
        }

        return _currentId;
    }
}