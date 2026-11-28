package com.dagmawi.inventory.service;

import com.dagmawi.inventory.dto.*;
import com.dagmawi.inventory.exception.InventoryOperationException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.dao.DuplicateKeyException;
import org.springframework.stereotype.Service;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;
import java.util.*;
import java.util.function.Supplier;

@Service
public class ReservationService {
    private final JdbcTemplate jdbc;
    private final TransactionTemplate transactions;
    public ReservationService(JdbcTemplate jdbc, PlatformTransactionManager manager) {
        this.jdbc=jdbc;
        this.transactions=new TransactionTemplate(manager);
    }

    public InventoryResponse updateStock(long productId, StockUpdateRequest request) {
        if (productId<=0 || request==null || request.availableQuantity()==null
                || request.availableQuantity()<0 || request.expectedVersion()==null || request.expectedVersion()<0)
            throw error(400,"Positive product ID, nonnegative quantity and expectedVersion are required.");
        return transactions.execute(tx -> {
            var current=lockStock(productId);
            if (current.version()!=request.expectedVersion())
                throw error(409,"Stock changed. Read the latest quantity and version before updating.");
            jdbc.update("UPDATE inventory SET available_quantity=?, version=version+1 WHERE product_id=?",
                    request.availableQuantity(),productId);
            return new InventoryResponse(productId,request.availableQuantity(),current.version()+1);
        });
    }

    public ReservationResponse reserve(ReserveInventoryRequest request) {
        if(request==null || request.reservationId()==null || request.items()==null
                || request.items().isEmpty() || request.items().size()>10)
            throw error(400,"A reservationId and between 1 and 10 items are required.");
        var items=new TreeMap<Long,Integer>();
        for(var item:request.items()) {
            if(item==null || item.productId()<=0 || item.quantity()<1 || item.quantity()>1000)
                throw error(400,"Each item needs a positive productId and quantity between 1 and 1000.");
            if(items.putIfAbsent(item.productId(),item.quantity())!=null)
                throw error(400,"Duplicate products are not allowed.");
        }
        return transactionWithDuplicateRetry(() -> {
            var id=request.reservationId();
            var status=lockReservation(id);
            if(status!=null) {
                var existing=readItems(id);
                if(!existing.equals(toItems(items)))
                    throw error(409,"Reservation ID already belongs to a different request.");
                if(!status.equals("RESERVED"))
                    throw error(409,"A released reservation cannot be reserved again. Use a new ID.");
                return new ReservationResponse(id,status,existing);
            }
            jdbc.update("INSERT INTO inventory_reservation(reservation_id,status) VALUES (?, 'RESERVED')",id);
            // Lock in product ID order in every operation to reduce deadlock risk.
            for(var item:items.entrySet()) {
                var stock=lockStock(item.getKey());
                if(stock.availableQuantity()<item.getValue()) throw error(409,"Insufficient stock for product "+item.getKey()+".");
                jdbc.update("UPDATE inventory SET available_quantity=available_quantity-?, version=version+1 WHERE product_id=?",
                        item.getValue(),item.getKey());
                jdbc.update("INSERT INTO inventory_reservation_item(reservation_id,product_id,quantity) VALUES (?,?,?)",
                        id,item.getKey(),item.getValue());
            }
            return new ReservationResponse(id,"RESERVED",toItems(items));
        });
    }

    public ReservationResponse get(UUID id) {
        return transactions.execute(tx -> {
            var status=lockReservation(id);
            if(status==null) throw error(404,"Reservation not found.");
            return new ReservationResponse(id,status,readItems(id));
        });
    }

    public ReservationResponse release(UUID id) {
        return transactionWithDuplicateRetry(() -> {
            var status=lockReservation(id);
            if(status==null) {
                jdbc.update("INSERT INTO inventory_reservation(reservation_id,status) VALUES (?, 'RELEASED')",id);
                return new ReservationResponse(id,"RELEASED",List.of());
            }
            var items=readItems(id);
            if(status.equals("RELEASED")) return new ReservationResponse(id,status,items);
            for(var item:items) {
                var stock=lockStock(item.productId());
                if((long)stock.availableQuantity()+item.quantity()>Integer.MAX_VALUE)
                    throw error(409,"Stock quantity would overflow.");
                jdbc.update("UPDATE inventory SET available_quantity=available_quantity+?, version=version+1 WHERE product_id=?",
                        item.quantity(),item.productId());
            }
            jdbc.update("UPDATE inventory_reservation SET status='RELEASED' WHERE reservation_id=?",id);
            return new ReservationResponse(id,"RELEASED",items);
        });
    }

    private <T> T transactionWithDuplicateRetry(Supplier<T> action) {
        // A competing insert can win the same ID. Retry once in a NEW transaction
        // after rollback so PostgreSQL's aborted transaction is never reused.
        try { return transactions.execute(tx -> action.get()); }
        catch(DuplicateKeyException collision) { return transactions.execute(tx -> action.get()); }
    }
    private InventoryResponse lockStock(long id) {
        var rows=jdbc.query("SELECT product_id,available_quantity,version FROM inventory WHERE product_id=? FOR UPDATE",
                (rs,n)->new InventoryResponse(rs.getLong(1),rs.getInt(2),rs.getLong(3)),id);
        if(rows.isEmpty()) throw error(404,"Inventory not found for product "+id+".");
        return rows.getFirst();
    }
    private String lockReservation(UUID id) {
        var rows=jdbc.query("SELECT status FROM inventory_reservation WHERE reservation_id=? FOR UPDATE",
                (rs,n)->rs.getString(1),id);
        return rows.isEmpty()?null:rows.getFirst();
    }
    private List<ReserveInventoryRequest.Item> readItems(UUID id) {
        return jdbc.query("SELECT product_id,quantity FROM inventory_reservation_item WHERE reservation_id=? ORDER BY product_id",
                (rs,n)->new ReserveInventoryRequest.Item(rs.getLong(1),rs.getInt(2)),id);
    }
    private List<ReserveInventoryRequest.Item> toItems(TreeMap<Long,Integer> items) {
        return items.entrySet().stream().map(item->new ReserveInventoryRequest.Item(item.getKey(),item.getValue())).toList();
    }
    private static InventoryOperationException error(int status,String message) {
        return new InventoryOperationException(status,message);
    }
}
