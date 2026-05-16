using System;
using System.Collections.Generic;
using System.Linq;

class IronmanSim {
    static Random rng = new Random();
    enum HeritageGroup { Aluvian=1, Gharundim=2, Sho=3, Viamontian=4, Shadowbound=5, Gearknight=6, Tumerok=7, Lugian=8, Empyrean=9, Penumbraen=10, Undead=11 }
    enum Skill { FinesseWeapons=46, LightWeapons=45, HeavyWeapons=44, TwoHandedCombat=41, MissileWeapons=47, VoidMagic=43, WarMagic=34, LifeMagic=33, ArmorTinkering=29, AssessCreature=27, Deception=20, DualWield=49, ItemTinkering=18, Leadership=35, MagicItemTinkering=30, MeleeDefense=6, MissileDefense=7, Shield=48, WeaponTinkering=28, Alchemy=38, Cooking=39, CreatureEnchantment=31, DirtyFighting=52, Fletching=37, Healing=21, ItemEnchantment=32, Lockpick=23, ManaConversion=16, SneakAttack=51, Summoning=54, ArcaneLore=14, Jump=22, Loyalty=36, MagicDefense=15, Run=24 }
    class WO { public Skill S; public int T; public bool M; public string N; }
    class PO { public Skill S; public int T, C; }
    static WO[] WPool = { new WO{S=Skill.FinesseWeapons,T=8,M=false,N="Finesse"}, new WO{S=Skill.LightWeapons,T=8,M=false,N="Light"}, new WO{S=Skill.HeavyWeapons,T=12,M=false,N="Heavy"}, new WO{S=Skill.TwoHandedCombat,T=16,M=false,N="2H"}, new WO{S=Skill.MissileWeapons,T=12,M=false,N="Missile"}, new WO{S=Skill.VoidMagic,T=28,M=true,N="Void"}, new WO{S=Skill.WarMagic,T=28,M=true,N="War"}, new WO{S=Skill.LifeMagic,T=20,M=true,N="Life"} };
    static PO[] PPool = { new PO{S=Skill.ArmorTinkering,T=4,C=0}, new PO{S=Skill.AssessCreature,T=4,C=2}, new PO{S=Skill.Deception,T=4,C=2}, new PO{S=Skill.DualWield,T=2,C=2}, new PO{S=Skill.ItemTinkering,T=2,C=0}, new PO{S=Skill.Leadership,T=4,C=2}, new PO{S=Skill.MagicItemTinkering,T=4,C=0}, new PO{S=Skill.MeleeDefense,T=10,C=10}, new PO{S=Skill.MissileDefense,T=6,C=4}, new PO{S=Skill.Shield,T=2,C=2}, new PO{S=Skill.WeaponTinkering,T=4,C=0}, new PO{S=Skill.Alchemy,T=6,C=6}, new PO{S=Skill.Cooking,T=4,C=4}, new PO{S=Skill.CreatureEnchantment,T=8,C=8}, new PO{S=Skill.DirtyFighting,T=2,C=2}, new PO{S=Skill.Fletching,T=4,C=4}, new PO{S=Skill.Healing,T=6,C=4}, new PO{S=Skill.ItemEnchantment,T=8,C=8}, new PO{S=Skill.LifeMagic,T=12,C=8}, new PO{S=Skill.Lockpick,T=6,C=4}, new PO{S=Skill.ManaConversion,T=6,C=6}, new PO{S=Skill.SneakAttack,T=4,C=2}, new PO{S=Skill.Summoning,T=8,C=4}, new PO{S=Skill.ArcaneLore,T=0,C=2}, new PO{S=Skill.Jump,T=0,C=4}, new PO{S=Skill.Loyalty,T=0,C=2}, new PO{S=Skill.MagicDefense,T=0,C=12}, new PO{S=Skill.Run,T=0,C=4} };
    static void Main() {
        Console.WriteLine("=== IRONMAN LOGIC SIMULATION ===\n");
        for(int i=0;i<5;i++) {
            var h = (HeritageGroup)rng.Next(1,12);
            var w = WPool[rng.Next(WPool.Length)];
            Console.WriteLine($"Sim #{i+1}: {h}, Weapon: {w.N} (Magic:{w.M})");
            var p = new List<PO>(PPool);
            if(w.M) { Console.WriteLine("  -> Removing ManaConversion from pool"); p.RemoveAll(x=>x.S==Skill.ManaConversion); }
            if(w.S==Skill.LifeMagic) { Console.WriteLine("  -> Removing LifeMagic from pool"); p.RemoveAll(x=>x.S==Skill.LifeMagic); }
            int s=0;
            do{ Shuffle(p); s++; }
            while(p.Count>0&&(p[0].C==0||p[0].S==Skill.ArmorTinkering||p[0].S==Skill.ItemTinkering||p[0].S==Skill.MagicItemTinkering||p[0].S==Skill.WeaponTinkering));
            if(w.M) { Console.WriteLine("  -> Inserting ManaConversion at front"); p.Insert(0,new PO{S=Skill.ManaConversion,T=6,C=6}); }
            Console.WriteLine($"  -> Shuffled {s}x, First: {p[0].S} (SpecCost:{p[0].C})");
            bool ok = p[0].C>0 && p[0].S!=Skill.ArmorTinkering && p[0].S!=Skill.ItemTinkering && p[0].S!=Skill.MagicItemTinkering && p[0].S!=Skill.WeaponTinkering && (!w.M || p[0].S==Skill.ManaConversion) && p.GroupBy(x=>x.S).All(g=>g.Count()==1);
            Console.WriteLine($"  -> {(ok?"VALID":"INVALID")}\n");
        }
    }
    static void Shuffle<T>(IList<T> l) { for(int i=l.Count-1;i>0;i--) { int j=rng.Next(i+1); var t=l[i]; l[i]=l[j]; l[j]=t; } }
}
