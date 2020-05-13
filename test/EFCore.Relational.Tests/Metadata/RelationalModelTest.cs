// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

// ReSharper disable InconsistentNaming
namespace Microsoft.EntityFrameworkCore.Metadata
{
    public class RelationalModelTest
    {
        [ConditionalTheory]
        [InlineData(true, Mapping.TPH)]
        [InlineData(true, Mapping.TPT)]
        [InlineData(false, Mapping.TPH)]
        [InlineData(false, Mapping.TPT)]
        public void Can_use_relational_model_with_tables(bool useExplicitMapping, Mapping mapping)
        {
            var model = CreateTestModel(mapToTables: useExplicitMapping, mapping: mapping);

            Assert.Equal(6, model.Model.GetEntityTypes().Count());
            Assert.Equal(mapping == Mapping.TPH || !useExplicitMapping ? 2 : 3, model.Tables.Count());
            Assert.Empty(model.Views);
            Assert.True(model.Model.GetEntityTypes().All(et => !et.GetViewMappings().Any()));

            AssertTables(model, useExplicitMapping ? mapping : Mapping.TPH);
        }

        [ConditionalTheory]
        [InlineData(Mapping.TPH)]
        [InlineData(Mapping.TPT)]
        public void Can_use_relational_model_with_views(Mapping mapping)
        {
            var model = CreateTestModel(mapToTables: false, mapToViews: true, mapping);

            Assert.Equal(6, model.Model.GetEntityTypes().Count());
            Assert.Equal(mapping == Mapping.TPH ? 2 : 3, model.Views.Count());
            Assert.Empty(model.Tables);
            Assert.True(model.Model.GetEntityTypes().All(et => !et.GetTableMappings().Any()));

            AssertViews(model, mapping);
        }

        [ConditionalTheory]
        [InlineData(Mapping.TPH)]
        [InlineData(Mapping.TPT)]
        public void Can_use_relational_model_with_views_and_tables(Mapping mapping)
        {
            var model = CreateTestModel(mapToTables: true, mapToViews: true, mapping);

            Assert.Equal(6, model.Model.GetEntityTypes().Count());
            Assert.Equal(mapping == Mapping.TPH ? 2 : 3, model.Tables.Count());
            Assert.Equal(mapping == Mapping.TPH ? 2 : 3, model.Views.Count());

            AssertTables(model, mapping);
            AssertViews(model, mapping);
        }

        private static void AssertViews(IRelationalModel model, Mapping mapping)
        {
            var orderType = model.Model.FindEntityType(typeof(Order));
            var orderMapping = orderType.GetViewMappings().Single();
            Assert.Same(orderType.GetViewMappings(), orderType.GetViewOrTableMappings());
            Assert.True(orderMapping.IncludesDerivedTypes);
            Assert.Equal(
                new[] { nameof(Order.CustomerId), nameof(Order.OrderDate), nameof(Order.OrderId) },
                orderMapping.ColumnMappings.Select(m => m.Property.Name));

            var ordersView = orderMapping.View;
            Assert.Same(ordersView, model.FindView(ordersView.Name, ordersView.Schema));
            Assert.Equal(
                new[] { "OrderDetails.BillingAddress#Address", "OrderDetails.ShippingAddress#Address", nameof(Order), nameof(OrderDetails) },
                ordersView.EntityTypeMappings.Select(m => m.EntityType.DisplayName()));
            Assert.Equal(new[] {
                    nameof(Order.CustomerId),
                    "Details_BillingAddress_City",
                    "Details_BillingAddress_Street",
                    "Details_ShippingAddress_City",
                    "Details_ShippingAddress_Street",
                    "OrderDateView",
                    nameof(Order.OrderId)
            },
                ordersView.Columns.Select(m => m.Name));
            Assert.Equal("OrderView", ordersView.Name);
            Assert.Equal("viewSchema", ordersView.Schema);
            Assert.Null(ordersView.ViewDefinition);

            var orderDate = orderType.FindProperty(nameof(Order.OrderDate));
            Assert.False(orderDate.IsViewColumnNullable());

            var orderDateMapping = orderDate.GetViewColumnMappings().Single();
            Assert.NotNull(orderDateMapping.TypeMapping);
            Assert.Equal("default_datetime_mapping", orderDateMapping.TypeMapping.StoreType);
            Assert.Same(orderMapping, orderDateMapping.ViewMapping);

            var orderDetailsOwnership = orderType.FindNavigation(nameof(Order.Details)).ForeignKey;
            var orderDetailsType = orderDetailsOwnership.DeclaringEntityType;
            Assert.Same(ordersView, orderDetailsType.GetViewMappings().Single().View);
            Assert.Equal(ordersView.GetReferencingInternalForeignKeys(orderType), ordersView.GetInternalForeignKeys(orderDetailsType));

            var orderDetailsDate = orderDetailsType.FindProperty(nameof(OrderDetails.OrderDate));
            Assert.True(orderDetailsDate.IsViewColumnNullable());

            var orderDateColumn = orderDateMapping.Column;
            Assert.Same(orderDateColumn, ordersView.FindColumn("OrderDateView"));
            Assert.Same(orderDateColumn, orderDate.FindViewColumn(ordersView.Name, ordersView.Schema));
            Assert.Equal(new[] { orderDate, orderDetailsDate }, orderDateColumn.PropertyMappings.Select(m => m.Property));
            Assert.Equal("OrderDateView", orderDateColumn.Name);
            Assert.Equal("default_datetime_mapping", orderDateColumn.StoreType);
            Assert.False(orderDateColumn.IsNullable);
            Assert.Same(ordersView, orderDateColumn.Table);

            var customerType = model.Model.FindEntityType(typeof(Customer));
            var customerView = customerType.GetViewMappings().Single().View;
            Assert.Equal("CustomerView", customerView.Name);
            Assert.Equal("viewSchema", customerView.Schema);

            var specialCustomerType = model.Model.FindEntityType(typeof(SpecialCustomer));
            var customerPk = specialCustomerType.FindPrimaryKey();

            if (mapping == Mapping.TPT)
            {
                Assert.Equal(2, specialCustomerType.GetViewMappings().Count());
                Assert.True(specialCustomerType.GetViewMappings().First().IsMainTableMapping);
                Assert.False(specialCustomerType.GetViewMappings().Last().IsMainTableMapping);

                var specialCustomerView = specialCustomerType.GetViewMappings().Select(t => t.Table).First(t => t.Name == "SpecialCustomerView");
                Assert.Null(specialCustomerView.Schema);
                Assert.Equal(3, specialCustomerView.Columns.Count());

                Assert.True(specialCustomerView.EntityTypeMappings.Single().IsMainEntityTypeMapping);

                var specialityColumn = specialCustomerView.Columns.Single(c => c.Name == nameof(SpecialCustomer.Speciality));
                Assert.False(specialityColumn.IsNullable);

                Assert.Null(customerType.GetDiscriminatorProperty());
                Assert.Null(customerType.GetDiscriminatorValue());
                Assert.Null(specialCustomerType.GetDiscriminatorProperty());
                Assert.Null(specialCustomerType.GetDiscriminatorValue());
            }
            else
            {
                var specialCustomerViewMapping = specialCustomerType.GetViewMappings().Single();
                Assert.True(specialCustomerViewMapping.IsMainTableMapping);

                var specialCustomerView = specialCustomerViewMapping.View;
                Assert.Same(customerView, specialCustomerView);

                Assert.Equal(2, specialCustomerView.EntityTypeMappings.Count());
                Assert.True(specialCustomerView.EntityTypeMappings.First().IsMainEntityTypeMapping);
                Assert.False(specialCustomerView.EntityTypeMappings.Last().IsMainEntityTypeMapping);

                var specialityColumn = specialCustomerView.Columns.Single(c => c.Name == nameof(SpecialCustomer.Speciality));
                Assert.True(specialityColumn.IsNullable);
            }
        }

        private static void AssertTables(IRelationalModel model, Mapping mapping)
        {
            var orderType = model.Model.FindEntityType(typeof(Order));
            var orderMapping = orderType.GetTableMappings().Single();
            Assert.True(orderMapping.IncludesDerivedTypes);
            Assert.Equal(
                new[] { nameof(Order.CustomerId), nameof(Order.OrderDate), nameof(Order.OrderId) },
                orderMapping.ColumnMappings.Select(m => m.Property.Name));

            var ordersTable = orderMapping.Table;
            Assert.Same(ordersTable, model.FindTable(ordersTable.Name, ordersTable.Schema));
            Assert.Equal(
                new[] { "OrderDetails.BillingAddress#Address", "OrderDetails.ShippingAddress#Address", nameof(Order), nameof(OrderDetails) },
                ordersTable.EntityTypeMappings.Select(m => m.EntityType.DisplayName()));
            Assert.Equal(new[] {
                    nameof(Order.CustomerId),
                    "Details_BillingAddress_City",
                    "Details_BillingAddress_Street",
                    "Details_ShippingAddress_City",
                    "Details_ShippingAddress_Street",
                    nameof(Order.OrderDate),
                    nameof(Order.OrderId)
            },
                ordersTable.Columns.Select(m => m.Name));
            Assert.Equal("Order", ordersTable.Name);
            Assert.Null(ordersTable.Schema);
            Assert.True(ordersTable.IsMigratable);
            Assert.True(ordersTable.IsSplit);

            var orderDate = orderType.FindProperty(nameof(Order.OrderDate));
            Assert.False(orderDate.IsColumnNullable());

            var orderDateMapping = orderDate.GetTableColumnMappings().Single();
            Assert.NotNull(orderDateMapping.TypeMapping);
            Assert.Equal("default_datetime_mapping", orderDateMapping.TypeMapping.StoreType);
            Assert.Same(orderMapping, orderDateMapping.TableMapping);

            var orderPk = orderType.GetKeys().Single();
            var orderPkConstraint = orderPk.GetMappedConstraints().Single();

            Assert.Equal("PK_Order", orderPkConstraint.Name);
            Assert.Equal(nameof(Order.OrderId), orderPkConstraint.Columns.Single().Name);
            Assert.Same(ordersTable, orderPkConstraint.Table);
            Assert.True(orderPkConstraint.IsPrimaryKey);
            Assert.Contains(orderPk, orderPkConstraint.MappedKeys);
            Assert.Same(orderPkConstraint, ordersTable.UniqueConstraints.Single());
            Assert.Same(orderPkConstraint, ordersTable.PrimaryKey);

            var orderIndex = orderType.GetIndexes().Single();
            var orderTableIndex = orderIndex.GetMappedTableIndexes().Single();

            Assert.Equal("IX_Order_CustomerId", orderTableIndex.Name);
            Assert.Equal(nameof(Order.CustomerId), orderTableIndex.Columns.Single().Name);
            Assert.Same(ordersTable, orderTableIndex.Table);
            Assert.False(orderTableIndex.IsUnique);
            Assert.Null(orderTableIndex.Filter);
            Assert.Equal(orderIndex, orderTableIndex.MappedIndexes.Single());
            Assert.Same(orderTableIndex, ordersTable.Indexes.Single());

            var orderFk = orderType.GetForeignKeys().Single();
            var orderFkConstraint = orderFk.GetMappedConstraints().Single();

            Assert.Equal("FK_Order_Customer_CustomerId", orderFkConstraint.Name);
            Assert.Equal(nameof(Order.CustomerId), orderFkConstraint.Columns.Single().Name);
            Assert.Equal(nameof(Customer.Id), orderFkConstraint.PrincipalColumns.Single().Name);
            Assert.Same(ordersTable, orderFkConstraint.Table);
            Assert.Equal("Customer", orderFkConstraint.PrincipalTable.Name);
            Assert.Equal(ReferentialAction.Cascade, orderFkConstraint.OnDeleteAction);
            Assert.Equal(orderFk, orderFkConstraint.MappedForeignKeys.Single());
            Assert.Same(orderFkConstraint, ordersTable.ForeignKeyConstraints.Single());

            var orderDetailsOwnership = orderType.FindNavigation(nameof(Order.Details)).ForeignKey;
            var orderDetailsType = orderDetailsOwnership.DeclaringEntityType;
            Assert.Same(ordersTable, orderDetailsType.GetTableMappings().Single().Table);
            Assert.Equal(ordersTable.GetReferencingInternalForeignKeys(orderType), ordersTable.GetInternalForeignKeys(orderDetailsType));
            Assert.Empty(orderDetailsOwnership.GetMappedConstraints());
            Assert.Empty(orderDetailsType.GetForeignKeys().Where(fk => fk != orderDetailsOwnership));
            Assert.Same(orderPkConstraint, orderDetailsType.FindPrimaryKey().GetMappedConstraints().Single());
            Assert.Contains(orderDetailsType.FindPrimaryKey(), orderPkConstraint.MappedKeys);

            var orderDetailsDate = orderDetailsType.FindProperty(nameof(OrderDetails.OrderDate));
            Assert.True(orderDetailsDate.IsColumnNullable());

            var orderDateColumn = orderDateMapping.Column;
            Assert.Same(orderDateColumn, ordersTable.FindColumn("OrderDate"));
            Assert.Same(orderDateColumn, orderDate.FindTableColumn(ordersTable.Name, ordersTable.Schema));
            Assert.Equal(new[] { orderDate, orderDetailsDate }, orderDateColumn.PropertyMappings.Select(m => m.Property));
            Assert.Equal("OrderDate", orderDateColumn.Name);
            Assert.Equal("default_datetime_mapping", orderDateColumn.StoreType);
            Assert.False(orderDateColumn.IsNullable);
            Assert.Same(ordersTable, orderDateColumn.Table);

            var customerType = model.Model.FindEntityType(typeof(Customer));
            var customerTable = customerType.GetTableMappings().Single().Table;
            Assert.Equal("Customer", customerTable.Name);

            var specialCustomerType = model.Model.FindEntityType(typeof(SpecialCustomer));
            var customerPk = specialCustomerType.FindPrimaryKey();

            if (mapping == Mapping.TPT)
            {
                Assert.Equal(2, specialCustomerType.GetTableMappings().Count());
                Assert.True(specialCustomerType.GetTableMappings().First().IsMainTableMapping);
                Assert.False(specialCustomerType.GetTableMappings().Last().IsMainTableMapping);

                var specialCustomerTable = specialCustomerType.GetTableMappings().Select(t => t.Table).First(t => t.Name == "SpecialCustomer");
                Assert.Equal("SpecialSchema", specialCustomerTable.Schema);
                Assert.Equal(3, specialCustomerTable.Columns.Count());

                Assert.True(specialCustomerTable.EntityTypeMappings.Single().IsMainEntityTypeMapping);

                var specialityColumn = specialCustomerTable.Columns.Single(c => c.Name == nameof(SpecialCustomer.Speciality));
                Assert.False(specialityColumn.IsNullable);

                Assert.Equal(2, customerPk.GetMappedConstraints().Count());
                var specialCustomerPkConstraint = specialCustomerTable.PrimaryKey;
                Assert.Equal("PK_SpecialCustomer", specialCustomerPkConstraint.Name);
                Assert.Same(specialCustomerPkConstraint.MappedKeys.Single(), customerPk);

                var idProperty = customerPk.Properties.Single();
                Assert.Equal(3, idProperty.GetTableColumnMappings().Count());

                Assert.Empty(customerTable.ForeignKeyConstraints);

                var specialCustomerUniqueConstraint = customerTable.UniqueConstraints.Single(c => !c.IsPrimaryKey);
                Assert.Equal("AK_Customer_SpecialityAk", specialCustomerUniqueConstraint.Name);
                Assert.NotNull(specialCustomerUniqueConstraint.MappedKeys.Single());

                var specialCustomerFkConstraint = specialCustomerTable.ForeignKeyConstraints.Single();
                Assert.Equal("FK_SpecialCustomer_Customer_RelatedCustomerSpeciality", specialCustomerFkConstraint.Name);
                Assert.Same(customerTable, specialCustomerFkConstraint.PrincipalTable);
                Assert.NotNull(specialCustomerFkConstraint.MappedForeignKeys.Single());

                var specialCustomerDbIndex = specialCustomerTable.Indexes.Single();
                Assert.Equal("IX_SpecialCustomer_RelatedCustomerSpeciality", specialCustomerDbIndex.Name);
                Assert.NotNull(specialCustomerDbIndex.MappedIndexes.Single());

                Assert.Null(customerType.GetDiscriminatorProperty());
                Assert.Null(customerType.GetDiscriminatorValue());
                Assert.Null(specialCustomerType.GetDiscriminatorProperty());
                Assert.Null(specialCustomerType.GetDiscriminatorValue());
            }
            else
            {
                var specialCustomerTypeMapping = specialCustomerType.GetTableMappings().Single();
                Assert.True(specialCustomerTypeMapping.IsMainTableMapping);

                var specialCustomerTable = specialCustomerTypeMapping.Table;
                Assert.Same(customerTable, specialCustomerTable);

                Assert.Equal(2, specialCustomerTable.EntityTypeMappings.Count());
                Assert.True(specialCustomerTable.EntityTypeMappings.First().IsMainEntityTypeMapping);
                Assert.False(specialCustomerTable.EntityTypeMappings.Last().IsMainEntityTypeMapping);

                var specialityColumn = specialCustomerTable.Columns.Single(c => c.Name == nameof(SpecialCustomer.Speciality));
                Assert.True(specialityColumn.IsNullable);

                var specialCustomerPkConstraint = specialCustomerTable.PrimaryKey;
                Assert.Equal("PK_Customer", specialCustomerPkConstraint.Name);
                Assert.Same(specialCustomerPkConstraint.MappedKeys.Single(), customerPk);

                var idProperty = customerPk.Properties.Single();
                Assert.Equal(2, idProperty.GetTableColumnMappings().Count());

                var specialCustomerUniqueConstraint = specialCustomerTable.UniqueConstraints.Single(c => !c.IsPrimaryKey);
                Assert.Equal("AK_Customer_SpecialityAk", specialCustomerUniqueConstraint.Name);
                Assert.NotNull(specialCustomerUniqueConstraint.MappedKeys.Single());

                var specialCustomerFkConstraint = specialCustomerTable.ForeignKeyConstraints.Single();
                Assert.Equal("FK_Customer_Customer_RelatedCustomerSpeciality", specialCustomerFkConstraint.Name);
                Assert.NotNull(specialCustomerFkConstraint.MappedForeignKeys.Single());

                var specialCustomerDbIndex = specialCustomerTable.Indexes.Single();
                Assert.Equal("IX_Customer_RelatedCustomerSpeciality", specialCustomerDbIndex.Name);
                Assert.NotNull(specialCustomerDbIndex.MappedIndexes.Single());
            }
        }

        private IRelationalModel CreateTestModel(bool mapToTables = false, bool mapToViews = false, Mapping mapping = Mapping.TPH)
        {
            var modelBuilder = CreateConventionModelBuilder();
            modelBuilder.Entity<Customer>(cb =>
            {
                if (mapToViews)
                {
                    cb.ToView("CustomerView", "viewSchema");
                }

                if (mapToTables)
                {
                    cb.ToTable("Customer");
                }

                cb.Property<string>("SpecialityAk");
            });

            modelBuilder.Entity<SpecialCustomer>(cb =>
            {
                if (mapToViews
                    && mapping == Mapping.TPT)
                {
                    cb.ToView("SpecialCustomerView");
                }

                if (mapToTables
                    && mapping == Mapping.TPT)
                {
                    cb.ToTable("SpecialCustomer", "SpecialSchema");
                }

                cb.Property(s => s.Speciality).IsRequired();

                cb.HasOne(c => c.RelatedCustomer).WithOne()
                    .HasForeignKey<SpecialCustomer>(c => c.RelatedCustomerSpeciality)
                    .HasPrincipalKey<SpecialCustomer>("SpecialityAk"); // TODO: Use the derived one, #2611
            });

            modelBuilder.Entity<Order>(ob =>
            {
                ob.Property(od => od.OrderDate).HasColumnName("OrderDate");
                ob.Property(od => od.OrderDate).HasViewColumnName("OrderDateView");

                ob.OwnsOne(o => o.Details, odb =>
                {
                    odb.Property(od => od.OrderDate).HasColumnName("OrderDate");
                    odb.Property(od => od.OrderDate).HasViewColumnName("OrderDateView");

                    odb.OwnsOne(od => od.BillingAddress);
                    odb.OwnsOne(od => od.ShippingAddress);
                });

                if (mapToViews)
                {
                    ob.ToView("OrderView", "viewSchema");
                }
                if (mapToTables)
                {
                    ob.ToTable("Order");
                }
            });

            return modelBuilder.FinalizeModel().GetRelationalModel();
        }

        [ConditionalFact]
        public void Can_use_relational_model_with_keyless_TPH()
        {
            var modelBuilder = CreateConventionModelBuilder();

            modelBuilder.Entity<Customer>(cb =>
            {
                cb.Ignore(c => c.Orders);
                cb.ToView("CustomerView");
            });

            modelBuilder.Entity<SpecialCustomer>(cb =>
            {
                cb.Property(s => s.Speciality).IsRequired();
            });

            var model = modelBuilder.FinalizeModel().GetRelationalModel();

            Assert.Equal(2, model.Model.GetEntityTypes().Count());
            Assert.Empty(model.Tables);
            Assert.Single(model.Views);

            var customerType = model.Model.FindEntityType(typeof(Customer));
            Assert.NotNull(customerType.GetDiscriminatorProperty());

            var customerView = customerType.GetViewMappings().Single().View;
            Assert.Equal("CustomerView", customerView.Name);

            var specialCustomerType = model.Model.FindEntityType(typeof(SpecialCustomer));

            var specialCustomerTypeMapping = specialCustomerType.GetViewMappings().Single();
            Assert.True(specialCustomerTypeMapping.IsMainTableMapping);

            var specialCustomerView = specialCustomerTypeMapping.View;
            Assert.Same(customerView, specialCustomerView);

            Assert.Equal(2, specialCustomerView.EntityTypeMappings.Count());
            Assert.True(specialCustomerView.EntityTypeMappings.First().IsMainEntityTypeMapping);
            Assert.False(specialCustomerView.EntityTypeMappings.Last().IsMainEntityTypeMapping);

            var specialityColumn = specialCustomerView.Columns.Single(c => c.Name == nameof(SpecialCustomer.Speciality));
            Assert.True(specialityColumn.IsNullable);
        }

        protected virtual ModelBuilder CreateConventionModelBuilder() => RelationalTestHelpers.Instance.CreateConventionBuilder();

        public enum Mapping
        {
            TPH,
            TPT,
            TPC
        }

        private enum MyEnum : ulong
        {
            Sun,
            Mon,
            Tue
        }

        private class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public short SomeShort { get; set; }
            public MyEnum EnumValue { get; set; }

            public IEnumerable<Order> Orders { get; set; }
        }

        private class SpecialCustomer : Customer
        {
            public string Speciality { get; set; }
            public string RelatedCustomerSpeciality { get; set; }
            public SpecialCustomer RelatedCustomer { get; set; }
        }

        private class Order
        {
            public int OrderId { get; set; }
            public DateTime OrderDate { get; set; }

            public int CustomerId { get; set; }
            public Customer Customer { get; set; }

            public OrderDetails Details { get; set; }
        }

        private class OrderDetails
        {
            public DateTime OrderDate { get; set; }

            public int OrderId { get; set; }
            public Order Order { get; set; }
            public Address BillingAddress { get; set; }
            public Address ShippingAddress { get; set; }
        }

        private class Address
        {
            public string Street { get; set; }
            public string City { get; set; }
        }
    }
}
