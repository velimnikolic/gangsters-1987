using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.City
{
    public interface IPathFinder
    {
        List<Path> GetPath(Vector3 vector);
    }
}
