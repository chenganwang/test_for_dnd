using System;

namespace CombatV051.Domain
{
    [Serializable] public sealed class RngState
    {
        public string algorithmVersion="pcg32-v1";
        public ulong state,increment,drawCount;
    }
    [Serializable] public sealed class RandomStreamsState
    {
        public string streamVersion="v051-streams-1";
        public ulong masterSeed;
        public RngState layout,contentReward,visibleTactical,backgroundCombat,classResource,eventDice,cosmetic;
    }
    public static class RandomStreams
    {
        public static RandomStreamsState Create(ulong seed)
        {
            return new RandomStreamsState {masterSeed=seed,layout=Stream(seed,"layout"),contentReward=Stream(seed,"content/reward"),
                visibleTactical=Stream(seed,"v051/visibleTactical"),backgroundCombat=Stream(seed,"v051/backgroundCombat"),
                classResource=Stream(seed,"v051/classResource"),eventDice=Stream(seed,"eventDice"),cosmetic=Stream(seed,"cosmetic")};
        }
        static ulong Hash(ulong seed,string label)
        {
            unchecked {ulong h=14695981039346656037UL;for(int i=0;i<8;i++){h^=(byte)(seed>>(8*i));h*=1099511628211UL;}foreach(char c in label){h^=(byte)c;h*=1099511628211UL;}return h;}
        }
        public static RngState Stream(ulong seed,string label)
        {
            var r=new RngState{increment=(Hash(seed,label+"/sequence")<<1)|1UL};NextUInt(r);unchecked{r.state+=Hash(seed,label+"/state");}NextUInt(r);r.drawCount=0;return r;
        }
        public static uint NextUInt(RngState r)
        {
            if(r==null||r.algorithmVersion!="pcg32-v1"||(r.increment&1UL)==0)throw new InvalidOperationException("invalid_random_stream");
            unchecked {ulong previous=r.state;r.state=previous*6364136223846793005UL+r.increment;uint x=(uint)(((previous>>18)^previous)>>27);int rotation=(int)(previous>>59);r.drawCount++;return(x>>rotation)|(x<<((-rotation)&31));}
        }
        public static int NextInt(RngState r,int minInclusive,int maxExclusive)
        {
            if(maxExclusive<=minInclusive)throw new ArgumentOutOfRangeException(nameof(maxExclusive));uint bound=(uint)((long)maxExclusive-minInclusive);uint threshold=unchecked(0u-bound)%bound;uint sample;do{sample=NextUInt(r);}while(sample<threshold);return(int)((long)minInclusive+sample%bound);
        }
        public static int D20(RngState r){return NextInt(r,1,21);}
    }
}
