internal struct PlainStruct
{
    private Guard _instance;                 // G01 instance field in a copyable struct
    private static Guard s_shared;           // G02 static field in a copyable struct

    public static void Use() => s_shared.Touch();
}

internal class PlainClass
{
    private Guard _instance;                 // G03 instance field in a class
    private static Guard s_shared;           // G04 static field in a class

    public void Use() { _instance.Touch(); s_shared.Touch(); }
}

internal record PlainRecord
{
    private static Guard s_shared;           // G05 static field in a record

    public static void Use() => s_shared.Touch();
}
