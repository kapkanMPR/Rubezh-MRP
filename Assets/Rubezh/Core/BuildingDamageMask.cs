namespace SilentOrders.Core
{
    // Presentation-only classification matching the historical per-pixel wall clip.
    public static class BuildingDamageMask
    {
        public static bool Removed(DynamicBuilding.Wall wall,int x,int z)
        {
            int index=z*wall.NX+x;
            if(wall.Initial[index]&&!wall.Solid[index])return true;
            if(wall.Solid[index])return false;
            for(int zz=System.Math.Max(0,z-1);zz<=System.Math.Min(wall.NZ-1,z+1);zz++)
                for(int xx=System.Math.Max(0,x-1);xx<=System.Math.Min(wall.NX-1,x+1);xx++)
                    if(wall.Solid[zz*wall.NX+xx])return false;
            return true;
        }
    }
}
