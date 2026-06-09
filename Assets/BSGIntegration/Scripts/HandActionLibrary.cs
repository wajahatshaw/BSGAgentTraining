using System;
using System.Collections.Generic;
using UnityEngine;

public static class HandActionLibrary
{
    public const int IndexFinger = 1;

    public static List<Vector3> GetRotationsForStep(ActionSequenceStep step, bool execute)
    {
        if (step == null || !execute)
            return Idle();

        string subtype = step.actionSubType ?? string.Empty;
        string target = step.targetObjectName ?? string.Empty;

        if (subtype.Equals("menu_open", StringComparison.OrdinalIgnoreCase))
            return MenuSelect();
        if (target.IndexOf("scroll", StringComparison.OrdinalIgnoreCase) >= 0)
            return Scroll();
        if (target.IndexOf("letter", StringComparison.OrdinalIgnoreCase) >= 0)
            return Type();
        if (target.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) >= 0)
            return Click();

        return Press();
    }

    public static List<Vector3> Idle()
    {
        return new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero };
    }

    static List<Vector3> Press()
    {
        return new List<Vector3>
        {
            new Vector3(0f, 0f, 30f),
            new Vector3(0f, 0f, 45f),
            new Vector3(0f, 0f, 20f)
        };
    }

    static List<Vector3> Click()
    {
        return new List<Vector3>
        {
            new Vector3(0f, 0f, 20f),
            new Vector3(0f, 0f, 35f),
            new Vector3(0f, 0f, 15f)
        };
    }

    static List<Vector3> Type()
    {
        return new List<Vector3>
        {
            new Vector3(0f, 0f, 15f),
            new Vector3(0f, 0f, 25f),
            new Vector3(0f, 0f, 10f)
        };
    }

    static List<Vector3> Scroll()
    {
        return new List<Vector3>
        {
            new Vector3(0f, 0f, 10f),
            new Vector3(15f, 0f, 0f),
            new Vector3(10f, 0f, 0f)
        };
    }

    static List<Vector3> MenuSelect()
    {
        return new List<Vector3>
        {
            new Vector3(-8f, 0f, 4f),
            new Vector3(-4f, 0f, 8f),
            new Vector3(0f, 0f, 3f)
        };
    }
}
