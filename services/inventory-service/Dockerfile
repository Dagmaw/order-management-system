FROM eclipse-temurin:25-jdk AS build

WORKDIR /workspace

COPY .mvn/ .mvn/
COPY mvnw bootstrap-maven.sh pom.xml ./
COPY src/ src/

RUN --mount=type=cache,target=/root/.m2 \
    sed -i 's/\r$//' mvnw bootstrap-maven.sh \
    && sh ./bootstrap-maven.sh \
    && sh ./mvnw --batch-mode --no-transfer-progress clean verify

FROM eclipse-temurin:25-jre AS final

WORKDIR /app

COPY --from=build /workspace/target/*.jar /app/app.jar

ADD --chmod=444 https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem /app/certs/global-bundle.pem
RUN chmod 755 /app/certs

EXPOSE 8080

USER 10001:10001
RUN test -r /app/certs/global-bundle.pem

ENTRYPOINT ["java", "-jar", "/app/app.jar"]