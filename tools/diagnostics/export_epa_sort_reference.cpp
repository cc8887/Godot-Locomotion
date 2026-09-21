// Build with the same MSVC toolchain used by the captured UE Editor target.
// No engine/plugin changes. Generates index permutations from the actual STL.
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <numeric>
#include <vector>
#include <cstdint>

int main(int argc, char** argv)
{
    if (argc != 2 || std::filesystem::exists(argv[1])) return 1;
    std::ofstream out(argv[1]); if (!out) return 2;
    out << "{\"msvc\":" << _MSC_FULL_VER << ",\"cases\":[";
    bool first = true;
    for (int count : {0,1,2,3,8,16,31,32,33,40,41,64,128,256,512,1024})
    for (int pattern=0; pattern<8; ++pattern)
    for (bool heap : {false,true})
    {
        std::vector<double> distances(count); std::vector<int> indices(count);
        std::iota(indices.begin(),indices.end(),0); uint32_t random=12345;
        for (int i=0;i<count;++i)
        {
            random = random * 1664525u + 1013904223u;
            switch (pattern)
            {
                case 0: distances[i]=0; break;
                case 1: distances[i]=i; break;
                case 2: distances[i]=count-i; break;
                case 3: distances[i]=i%3-1; break;
                case 4: distances[i]=static_cast<int>(random%11)-5; break;
                case 5: distances[i]=random; break;
                case 6: distances[i]=std::min(i,count-i); break;
                case 7: distances[i]=(i%2 ? -i : i); break;
            }
        }
        auto before=[&](int a,int b){return distances[a]>distances[b];};
        if (heap && count>0) std::_Sort_unchecked(indices.data(),indices.data()+count,ptrdiff_t(0),before);
        else std::sort(indices.begin(),indices.end(),before);
        if(!first)out<<",";first=false;
        out<<"{\"ideal\":"<<(heap?0:count)<<",\"distances\":[";
        for(int i=0;i<count;++i){if(i)out<<",";out<<std::fixed<<distances[i];}
        out<<"],\"indices\":[";
        for(int i=0;i<count;++i){if(i)out<<",";out<<indices[i];}
        out<<"]}";
    }
    out<<"]}";return out?0:3;
}
