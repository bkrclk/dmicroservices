﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DMicroservices.DataAccess.History;
using DMicroservices.Utils.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DMicroservices.DataAccess.Repository
{
    /// <summary>
    /// EntityFramework için hazırlıyor olduğumuz bu repositoriyi daha önceden tasarladığımız generic repositorimiz olan IRepository arayüzünü implemente ederek tasarladık.
    /// Bu şekilde tasarlamamızın ana sebebi ise veritabanına independent(bağımsız) bir durumda kalabilmek.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class FilteredRepository<T> : IRepository<T> where T : class
    {
        private readonly DbContext DbContext;
        private readonly DbSet<T> DbSet;

        public string FilterColumnName { get; set; }
        public object FilterColumnValue { get; set; }

        private PropertyInfo FilterProperty;
        /// <summary>
        /// Repository instance ı başlatırç
        /// </summary>
        /// <param name="dbContext">Veritabanı bağlantı nesnesi</param>
        /// <param name="filterColumnName"></param>
        /// <param name="filterValue"></param>
        public FilteredRepository(DbContext dbContext, string filterColumnName, object filterValue)
        {
            DbContext = dbContext;
            DbSet = dbContext.Set<T>();
            FilterColumnName = filterColumnName;
            FilterColumnValue = filterValue;
            FilterProperty = typeof(T).GetProperty(FilterColumnName);
        }

        public void Add(T entity)
        {
            if (FilterProperty != null)
                FilterProperty.SetValue(entity, FilterColumnValue);
            DbSet.Add(entity);
        }

        public void BulkInsert(List<T> entityList)
        {
            foreach (var entity in entityList)
            {
                if (FilterProperty != null)
                    FilterProperty.SetValue(entity, FilterColumnValue);
                DbSet.Add(entity);
            }
        }

        /// <summary>
        /// Aynı kayıt eklememek için objeyi kontrol ederek true veya false dönderir.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public bool Any(Expression<Func<T, bool>> predicate)
        {
            return DbSet.Where(GetFilterExpression()).Any(predicate);
        }

        public DbContext GetDbContext()
        {
            return DbContext;
        }

        public List<string> GetIncludePaths()
        {
            return DbContext.GetIncludePaths(typeof(T)).ToList();
        }

        public int Count()
        {
            return Count(arg => true);
        }

        public int Count(Expression<Func<T, bool>> predicate)
        {
            IQueryable<T> iQueryable = DbSet.Where(GetFilterExpression())
            .Where(predicate);
            return iQueryable.Count();
        }

        public void Delete(T entity, bool forceDelete = false)
        {

            if (FilterProperty != null)
                FilterProperty.SetValue(entity, FilterColumnValue);

            // Önce entity'nin state'ini kontrol etmeliyiz.
            EntityEntry<T> dbEntityEntry = DbContext.Entry(entity);

            if (dbEntityEntry.State != EntityState.Deleted)
            {
                dbEntityEntry.State = EntityState.Deleted;
            }
            else
            {
                DbSet.Attach(entity);
                DbSet.Remove(entity);
            }
        }

        public void Delete(Expression<Func<T, bool>> predicate, bool forceDelete = false)
        {
            T model = DbSet.FirstOrDefault(predicate);

            if (model != null)
                Delete(model, forceDelete);
        }

        public void BulkDelete(List<T> entityList)
        {
            foreach (var entity in entityList)
            {
                if (FilterProperty != null)
                    FilterProperty.SetValue(entity, FilterColumnValue);
                DbSet.Remove(entity);
            }
        }

        public T Get(Expression<Func<T, bool>> predicate)
        {
            IQueryable<T> iQueryable = DbSet.Where(GetFilterExpression())
                .Where(predicate);
            return iQueryable.ToList().FirstOrDefault();
        }

        public T Get(Expression<Func<T, bool>> predicate, List<string> includePaths)
        {
            IQueryable<T> iQueryable = DbSet
                .Where(predicate).Where(GetFilterExpression()).Include(includePaths);
            return iQueryable.ToList().FirstOrDefault();
        }

        public IQueryable<T> GetAll()
        {
            IQueryable<T> iQueryable = DbSet.Where(GetFilterExpression()).Where(x => x != null);
            return iQueryable;
        }

        public IQueryable<T> GetAll(System.Linq.Expressions.Expression<Func<T, bool>> predicate)
        {
            IQueryable<T> iQueryable = DbSet.Where(GetFilterExpression())
                .Where(predicate);
            return iQueryable;
        }

        public IQueryable<T> GetAll(System.Linq.Expressions.Expression<Func<T, bool>> predicate, List<string> includePaths)
        {
            IQueryable<T> iQueryable = DbSet
                .Where(GetFilterExpression()).Where(predicate).Include(includePaths);
            return iQueryable;
        }

        public IQueryable<dynamic> SelectList(Expression<Func<T, bool>> where, Expression<Func<T, dynamic>> select)
        {
            throw new NotImplementedException();
        }

        public List<T> SendSql(string sqlQuery)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Clid ile yalıtabilmek için CLID ile ilgili expression oluşturur.
        /// </summary>
        /// <returns></returns>
        private Expression<Func<T, bool>> GetFilterExpression()
        {
            if (FilterProperty != null)
            {
                ParameterExpression argParams = Expression.Parameter(typeof(T), "x");
                Expression filterProp = Expression.Property(argParams, FilterColumnName);
                ConstantExpression filterValue = Expression.Constant(FilterColumnValue);

                BinaryExpression filterExpression = Expression.Equal(filterProp, filterValue);
                return Expression.Lambda<Func<T, bool>>(filterExpression, argParams);
            }

            return x => true;
        }


        /// <summary>
        /// Verilen veriyi context üzerinde günceller.
        /// </summary>
        /// <param name="entity">Güncellenecek entity</param>
        public void Update(T entity)
        {
            if (FilterProperty != null)
                FilterProperty.SetValue(entity, FilterColumnValue);

            DbSet.Attach(entity);
            DbContext.Entry(entity).State = EntityState.Modified;
        }

        public void Update(Expression<Func<T, bool>> predicate, T entity)
        {
            throw new NotImplementedException();
        }
    }
}
