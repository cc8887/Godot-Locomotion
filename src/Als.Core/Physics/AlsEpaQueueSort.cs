namespace GodotAls.Core.Physics;

// Match the captured UE Windows toolchain's std::sort tie permutation
// (MSVC 14.44). Array.Sort's different partitioning changes EPA face identity.
internal static class AlsEpaQueueSort
{
    internal static void Sort(AlsEpaWorkspace w) => Sort(w,0,w.QueueCount,w.QueueCount);
    private static bool Before(AlsEpaWorkspace w, int a, int b) => w.Faces[a].Distance > w.Faces[b].Distance;
    private static bool At(AlsEpaWorkspace w,int a,int b) => Before(w,w.Queue[a],w.Queue[b]);
    private static void Swap(AlsEpaWorkspace w,int a,int b) => (w.Queue[a],w.Queue[b])=(w.Queue[b],w.Queue[a]);
    private static void Median(AlsEpaWorkspace w,int a,int b,int c)
    { if(At(w,b,a))Swap(w,b,a); if(At(w,c,b)){Swap(w,c,b);if(At(w,b,a))Swap(w,b,a);} }
    private static void Guess(AlsEpaWorkspace w,int first,int mid,int last)
    {
        var count=last-first;
        if(count>40)
        {
            var step=(count+1)>>3;var twice=step<<1;
            Median(w,first,first+step,first+twice);Median(w,mid-step,mid,mid+step);
            Median(w,last-twice,last-step,last);Median(w,first+step,mid,last-step);
        }
        else Median(w,first,mid,last);
    }
    private static (int,int) Partition(AlsEpaWorkspace w,int first,int last)
    {
        var mid=first+((last-first)>>1);Guess(w,first,mid,last-1);var pf=mid;var pl=pf+1;
        while(first<pf&&!At(w,pf-1,pf)&&!At(w,pf,pf-1))--pf;
        while(pl<last&&!At(w,pl,pf)&&!At(w,pf,pl))++pl;
        var gf=pl;var gl=pf;
        while(true)
        {
            for(;gf<last;++gf)
            {
                if(At(w,pf,gf))continue;
                if(At(w,gf,pf))break;
                if(pl!=gf)Swap(w,pl,gf);++pl;
            }
            for(;first<gl;--gl)
            {
                if(At(w,gl-1,pf))continue;
                if(At(w,pf,gl-1))break;
                if(--pf!=gl-1)Swap(w,pf,gl-1);
            }
            if(gl==first&&gf==last)return(pf,pl);
            if(gl==first)
            {if(pl!=gf)Swap(w,pf,pl);++pl;Swap(w,pf,gf);++pf;++gf;}
            else if(gf==last)
            {if(--gl!=--pf)Swap(w,gl,pf);Swap(w,pf,--pl);}
            else {Swap(w,gf,--gl);++gf;}
        }
    }
    internal static void Sort(AlsEpaWorkspace w,int first,int last,int ideal)
    {
        while(true)
        {
            if(last-first<=32)
            {
                for(var i=first+1;i<last;i++)
                {var value=w.Queue[i];var j=i;while(j>first&&Before(w,value,w.Queue[j-1])){w.Queue[j]=w.Queue[j-1];--j;}w.Queue[j]=value;}
                return;
            }
            if(ideal<=0){Heap(w,first,last);return;}
            var (left,right)=Partition(w,first,last);ideal=(ideal>>1)+(ideal>>2);
            if(left-first<last-right){Sort(w,first,left,ideal);first=right;}
            else{Sort(w,right,last,ideal);last=left;}
        }
    }
    private static void Hole(AlsEpaWorkspace w,int first,int hole,int bottom,int value)
    {
        var top=hole;var index=hole;var nonleaf=(bottom-1)>>1;
        while(index<nonleaf)
        {index=2*index+2;if(At(w,first+index,first+index-1))--index;w.Queue[first+hole]=w.Queue[first+index];hole=index;}
        if(index==nonleaf&&bottom%2==0){w.Queue[first+hole]=w.Queue[first+bottom-1];hole=bottom-1;}
        for(var parent=(hole-1)>>1;top<hole&&Before(w,w.Queue[first+parent],value);parent=(hole-1)>>1)
        {w.Queue[first+hole]=w.Queue[first+parent];hole=parent;}
        w.Queue[first+hole]=value;
    }
    private static void Heap(AlsEpaWorkspace w,int first,int last)
    {
        var count=last-first;
        for(var hole=count>>1;hole>0;){--hole;Hole(w,first,hole,count,w.Queue[first+hole]);}
        while(count>=2){--count;var value=w.Queue[first+count];w.Queue[first+count]=w.Queue[first];Hole(w,first,0,count,value);}
    }
}
