using System.Text.Json;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsEpaQueueSortTests
{
    [Fact]
    public void FaceTieOrderMatchesActualMsvcInsertionPartitionAndHeapPaths()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/Physics/epa_sort_msvc.json")));
        var count=0;
        foreach(var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var distances=row.GetProperty("distances");var size=distances.GetArrayLength();
            var w=new AlsEpaWorkspace();AlsEpaWorkspace.Ensure(ref w.Faces,size);AlsEpaWorkspace.Ensure(ref w.Queue,size);w.QueueCount=size;
            for(var i=0;i<size;i++){w.Faces[i].Distance=distances[i].GetDouble();w.Queue[i]=i;}
            AlsEpaQueueSort.Sort(w,0,size,row.GetProperty("ideal").GetInt32());
            Assert.Equal(row.GetProperty("indices").EnumerateArray().Select(x=>x.GetInt32()),w.Queue.Take(size));count++;
        }
        Assert.Equal(256,count);
    }
}
