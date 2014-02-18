﻿// Copyright 2006 Herre Kuijpers - <herre@xs4all.nl>
//
// This source file(s) may be redistributed, altered and customized
// by any means PROVIDING the authors name and all copyright
// notices remain intact.
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED. USE IT AT YOUR OWN RISK. THE AUTHOR ACCEPTS NO
// LIABILITY FOR ANY DATA DAMAGE/LOSS THAT THIS PRODUCT MAY CAUSE.
//-----------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.VectorMath;

namespace MatterHackers.RayTracer
{
    public class CompareCentersOnAxis : IComparer<IRayTraceable>
    {
        int whichAxis;
        public int WhichAxis
        {
            get
            {
                return whichAxis;
            }
            set
            {
                whichAxis = value % 3;
            }
        }

        public CompareCentersOnAxis(int whichAxis)
        {
            this.whichAxis = whichAxis % 3;
        }

        public int Compare(IRayTraceable a, IRayTraceable b)
        {
            if (a == null || b == null)
            {
                throw new Exception();
            }

            double axisCenterA = a.GetAxisAlignedBoundingBox().GetCenter()[whichAxis];
            double axisCenterB = b.GetAxisAlignedBoundingBox().GetCenter()[whichAxis];

            if (axisCenterA > axisCenterB)
            {
                return 1;
            }
            else if (axisCenterA < axisCenterB)
            {
                return -1;
            }
            return 0;
        }
    }

    /// <summary>
    /// element in a scene
    /// </summary>
    public interface IRayTraceable
    {
        /// <summary>
        /// Get the color for a primitive at the given info.
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        RGBA_Floats GetColor(IntersectInfo info);

        /// <summary>
        /// Specifies the ambient and diffuse color of the element.
        /// </summary>
        IMaterial Material { get; set; }

        bool GetContained(List<IRayTraceable> results, AxisAlignedBoundingBox subRegion);

        /// <summary>
        /// This method is to be implemented by each element seperately. This is the core
        /// function of each element, to determine the intersection with a ray.
        /// </summary>
        /// <param name="ray">the ray that intersects with the element</param>
        /// <returns></returns>
        IntersectInfo GetClosestIntersection(Ray ray);

        int FindFirstRay(RayBundle rayBundle, int rayIndexToStartCheckingFrom);
        void GetClosestIntersections(RayBundle ray, int rayIndexToStartCheckingFrom, IntersectInfo[] intersectionsForBundle);

        IEnumerable IntersectionIterator(Ray ray);

        double GetSurfaceArea();
        AxisAlignedBoundingBox GetAxisAlignedBoundingBox();

        /// <summary>
        /// This is the computation cost of doing an intersection with the given type.
        /// This number is the number of milliseconds it takes to do some number of intersections.
        /// It just needs to be the same number for every type as they only need to
        /// be relative to each other.
        /// It really does not need to be a member variable as it is fixed to a given
        /// type of object.  But it needs to be virtual so we can get to the value
        /// for a given class. (If only there were class virtual functions :) ).
        /// </summary>
        /// <returns></returns>
        double GetIntersectCost();
    }
}