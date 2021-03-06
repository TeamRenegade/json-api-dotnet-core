using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JsonApiDotNetCore.Extensions;
using JsonApiDotNetCore.Internal;
using JsonApiDotNetCore.Internal.Generics;
using JsonApiDotNetCore.Internal.Query;
using JsonApiDotNetCore.Models;
using JsonApiDotNetCore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JsonApiDotNetCore.Data
{
    public class DefaultEntityRepository<TEntity> 
        : DefaultEntityRepository<TEntity, int>,
        IEntityRepository<TEntity> 
        where TEntity : class, IIdentifiable<int>
    {
        public DefaultEntityRepository(
            DbContext context,
            ILoggerFactory loggerFactory,
            IJsonApiContext jsonApiContext)
        : base(context, loggerFactory, jsonApiContext)
        { }
    }

    public class DefaultEntityRepository<TEntity, TId> 
        : IEntityRepository<TEntity, TId> 
        where TEntity : class, IIdentifiable<TId>
    {
        private readonly DbContext _context;
        private readonly DbSet<TEntity> _dbSet;
        private readonly ILogger _logger;
        private readonly IJsonApiContext _jsonApiContext;
        private readonly IGenericProcessorFactory _genericProcessorFactory;

        public DefaultEntityRepository(
            DbContext context,
            ILoggerFactory loggerFactory,
            IJsonApiContext jsonApiContext)
        {
            _context = context;
            _dbSet = context.GetDbSet<TEntity>();
            _jsonApiContext = jsonApiContext;
            _logger = loggerFactory.CreateLogger<DefaultEntityRepository<TEntity, TId>>();
            _genericProcessorFactory = _jsonApiContext.GenericProcessorFactory;
        }

        public virtual IQueryable<TEntity> Get()
        {
            return _dbSet.Select(_jsonApiContext.QuerySet?.Fields);
        }

        public virtual IQueryable<TEntity> Filter(IQueryable<TEntity> entities,  FilterQuery filterQuery)
        {
            if(filterQuery == null)
                return entities;

            if(filterQuery.IsAttributeOfRelationship)
                return entities.Filter(new RelatedAttrFilterQuery(_jsonApiContext, filterQuery));
            
            return entities.Filter(new AttrFilterQuery(_jsonApiContext, filterQuery));
        }

        public virtual IQueryable<TEntity> Sort(IQueryable<TEntity> entities, List<SortQuery> sortQueries)
        {
            if(sortQueries == null || sortQueries.Count == 0)
                return entities;

            var orderedEntities = entities.Sort(sortQueries[0]);

            if (sortQueries.Count <= 1) return orderedEntities;

            for(var i=1; i < sortQueries.Count; i++)
                orderedEntities = orderedEntities.Sort(sortQueries[i]);

            return orderedEntities;
        }

        public virtual async Task<TEntity> GetAsync(TId id)
        {
            return await Get().SingleOrDefaultAsync(e => e.Id.Equals(id));
        }

        public virtual async Task<TEntity> GetAndIncludeAsync(TId id, string relationshipName)
        {
            return await Get()
                .Include(relationshipName)
                .SingleOrDefaultAsync(e => e.Id.Equals(id));
        }

        public virtual async Task<TEntity> CreateAsync(TEntity entity)
        {
            _dbSet.Add(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public virtual async Task<TEntity> UpdateAsync(TId id, TEntity entity)
        {
            var oldEntity = await GetAsync(id);

            if (oldEntity == null)
                return null;

            _jsonApiContext.RequestEntity.Attributes.ForEach(attr =>
            {
                attr.SetValue(oldEntity, attr.GetValue(entity));
            });

            foreach(var relationship in _jsonApiContext.RelationshipsToUpdate)
                relationship.Key.SetValue(oldEntity, relationship.Value);

            await _context.SaveChangesAsync();

            return oldEntity;
        }

        public async Task UpdateRelationshipsAsync(object parent, RelationshipAttribute relationship, IEnumerable<string> relationshipIds)
        {
            var genericProcessor = _genericProcessorFactory.GetProcessor(relationship.Type);
            await genericProcessor.UpdateRelationshipsAsync(parent, relationship, relationshipIds);
        }

        public virtual async Task<bool> DeleteAsync(TId id)
        {
            var entity = await GetAsync(id);

            if (entity == null)
                return false;

            _dbSet.Remove(entity);

            await _context.SaveChangesAsync();

            return true;
        }

        public virtual IQueryable<TEntity> Include(IQueryable<TEntity> entities, string relationshipName)
        {
            var entity = _jsonApiContext.RequestEntity;
            var relationship = entity.Relationships.FirstOrDefault(r => r.PublicRelationshipName == relationshipName);
            if(relationship != null)
                return entities.Include(relationship.InternalRelationshipName);
            
            throw new JsonApiException("400", $"Invalid relationship {relationshipName} on {entity.EntityName}",
                $"{entity.EntityName} does not have a relationship named {relationshipName}");
        }

        public virtual async Task<IEnumerable<TEntity>> PageAsync(IQueryable<TEntity> entities, int pageSize, int pageNumber)
        {
            if(pageSize > 0)
                return await entities
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

            return await entities.ToListAsync();
        }
    }
}
