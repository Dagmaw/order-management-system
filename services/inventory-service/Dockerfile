FROM eclipse-temurin:25-jdk AS build

WORKDIR /workspace

COPY .mvn/ .mvn/
COPY mvnw pom.xml ./
COPY src/ src/

RUN sed -i 's/\r$//' mvnw \
    && sh ./mvnw --batch-mode clean verify

FROM eclipse-temurin:25-jre AS final

WORKDIR /app

COPY --from=build /workspace/target/*.jar /app/app.jar

ADD --chmod=444 https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem /app/certs/global-bundle.pem

EXPOSE 8080

USER 10001:10001

ENTRYPOINT ["java", "-jar", "/app/app.jar"]